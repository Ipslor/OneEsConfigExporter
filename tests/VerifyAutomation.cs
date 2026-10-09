using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Forms;
using OneCConfigExporter;
class VerifyAutomation {
 static BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;static Type main;
 static Control C(object f,string n){return (Control)main.GetField(n,F).GetValue(f);}
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Wait(Task t){var end=DateTime.UtcNow.AddSeconds(15);while(!t.IsCompleted&&DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(20);}Check(t.IsCompleted,"Async timeout");t.GetAwaiter().GetResult();}
 [STAThread] static void Main(string[] args){
  string root=Path.Combine(args[0],"automation-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var asm=typeof(UserSettings).Assembly;main=asm.GetType("OneCConfigExporter.MainForm");var generator=asm.GetType("OneCConfigExporter.AutomaticScenario");
  var settings=new UserSettings{Platform=args[1],GithubRepositories=new[]{"owner/one","owner/two"},BatchBases=new List<BatchBase>{new BatchBase{Name="Успех",FileMode=false,Server="test",Database="one",Output=Path.Combine(root,"one")},new BatchBase{Name="Ошибка",FileMode=false,Server="test",Database="fail",Output=Path.Combine(root,"fail")},new BatchBase{Name="Пропуск",FileMode=false,Server="test",Database="skip",Enabled=false,Output=Path.Combine(root,"skip")}}};
  string script=Path.Combine(root,"run.ps1");string config=(string)generator.GetMethod("Write").Invoke(null,new object[]{script,asm.Location,settings});
  Check(File.Exists(script)&&File.Exists(config)&&!File.ReadAllText(script).Contains("test-secret"),"Script and encrypted snapshot");
  var form=(Form)Activator.CreateInstance(main,true);var handle=form.Handle;main.GetField("settingsPath",F).SetValue(form,config);main.GetMethod("LoadDefaults",F).Invoke(form,null);Check(((string[])main.GetField("githubRepositoryCache",F).GetValue(form)).Length==2,"Repository cache restored");
  var source=settings.BatchBases[0];var copy=(BatchBase)main.GetMethod("MakeCopy",F).Invoke(form,new object[]{source,true});Check(copy.Incremental&&copy.Enabled&&copy.Output==source.Output&&!source.Incremental,"Incremental copy and unchanged original");
  string result=Path.Combine(root,"headless.result.json");var task=(Task<int>)main.GetMethod("RunAutomatic").Invoke(form,new object[]{config,result});Wait(task);Check(task.Result==1&&File.ReadAllText(result).Contains("Не выбрана")&&!Directory.Exists(settings.BatchBases[2].Output),"Headless partial failure");
  form.Dispose();
  settings.BatchBases.RemoveAt(1);settings.BatchBases[0].Output=Path.Combine(root,"native-one");generator.GetMethod("Write").Invoke(null,new object[]{script,asm.Location,settings});
  var p=Process.Start(new ProcessStartInfo{FileName=asm.Location,Arguments="--run-batch --settings \""+config+"\" --result \""+result+"\"",UseShellExecute=false,CreateNoWindow=true});Check(p.WaitForExit(15000),"Native CLI timeout");Check(p.ExitCode==0&&File.ReadAllText(result).Contains("\"ExitCode\":0"),"Native headless success");p.Dispose();
  var nofile=Process.Start(new ProcessStartInfo{FileName=asm.Location,Arguments="--run-batch --settings \""+Path.Combine(root,"missing.dat")+"\" --result \""+result+"\"",UseShellExecute=false,CreateNoWindow=true});Check(nofile.WaitForExit(15000)&&nofile.ExitCode==2,"Missing snapshot fails without dialog");nofile.Dispose();
  string repo=settings.BatchBases[0].Output;foreach(string command in new[]{"init --initial-branch=main","config user.name Test","config user.email test@example.invalid","add Module.bsl","commit -m test"}){var git=Process.Start(new ProcessStartInfo{FileName=args[2],Arguments="-C \""+repo+"\" "+command,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true});git.StandardOutput.ReadToEnd();git.StandardError.ReadToEnd();git.WaitForExit();Check(git.ExitCode==0,"Git fixture");git.Dispose();}
  var dated=(Form)Activator.CreateInstance(main,true);var datedHandle=dated.Handle;main.GetField("batchBases",F).SetValue(dated,settings.BatchBases);C(dated,"gitPath").Text=args[2];main.GetField("gitReady",F).SetValue(dated,true);main.GetMethod("ReloadBatchRows",F).Invoke(dated,null);Wait((Task)main.GetMethod("RefreshLastChanges",F).Invoke(dated,null));var grid=(DataGridView)C(dated,"batchGrid");Check(Convert.ToString(grid.Rows[0].Cells["Последнее изменение"].Value)!="—","Commit date read");dated.Dispose();
  Console.WriteLine("PASS: cache persistence; incremental copy; encrypted scenario; headless partial failure; native CLI success; missing-settings exit 2; commit date.");
  Console.WriteLine("SCRIPT="+script);
 }
}
