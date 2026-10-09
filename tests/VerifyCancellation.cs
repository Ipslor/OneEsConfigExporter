using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Forms;
using OneCConfigExporter;
class VerifyCancellation {
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool value,string text){if(!value)throw new Exception(text);}
 static void Wait(Task task){var end=DateTime.UtcNow.AddSeconds(60);while(!task.IsCompleted&&DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(10);}Check(task.IsCompleted,"Timeout");task.GetAwaiter().GetResult();}
 [STAThread]static void Main(string[] args){
  string root=Path.Combine(args[0],"cancellation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string output=Path.Combine(root,"one");Directory.CreateDirectory(Path.Combine(output,".git"));var main=typeof(UserSettings).Assembly.GetType("OneCConfigExporter.MainForm");string previous=Environment.GetEnvironmentVariable("EXPORTER_TEST_SLOW_GIT");Environment.SetEnvironmentVariable("EXPORTER_TEST_SLOW_GIT","1");
  try{using(var form=(Form)Activator.CreateInstance(main,true)){
   var handle=form.Handle;((ComboBox)main.GetField("platform",flags).GetValue(form)).Text=args[1];((TextBox)main.GetField("gitPath",flags).GetValue(form)).Text=args[2];main.GetField("gitReady",flags).SetValue(form,true);
   main.GetField("batchBases",flags).SetValue(form,new List<BatchBase>{new BatchBase{Name="Slow Git",FileMode=false,Server="test",Database="one",Output=output,Commit=true},new BatchBase{Name="Must skip",FileMode=false,Server="test",Database="two",Output=Path.Combine(root,"two")}});main.GetMethod("ReloadBatchRows",flags).Invoke(form,null);
   var queue=(Task)main.GetMethod("RunBatchAsync",flags).Invoke(form,new object[]{false});var end=DateTime.UtcNow.AddSeconds(40);Process current=null;while(DateTime.UtcNow<end&&!queue.IsCompleted){Application.DoEvents();current=main.GetField("activeGitProcess",flags).GetValue(form) as Process;if(current!=null&&current.StartInfo.Arguments.Contains("status --porcelain"))break;current=null;Thread.Sleep(10);}Check(current!=null&&!current.HasExited,"Slow Git status active, not a preliminary index check");var designer=main.GetField("activeProcess",flags).GetValue(form) as Process;Check(designer!=null&&designer.HasExited,"Designer already exited before Git");Check(main.GetMethod("CancellationTarget",flags).Invoke(form,null)==current,"Stop targets Git, not exited Designer");int pid=current.Id;Wait((Task)main.GetMethod("RequestBatchStop",flags).Invoke(form,null));Wait(queue);Check(!Directory.Exists(Path.Combine(root,"two")),"Remaining base not started");bool alive=false;try{using(var p=Process.GetProcessById(pid))alive=!p.HasExited;}catch(ArgumentException){}Check(!alive,"Owned Git terminated");Check(main.GetField("activeGitProcess",flags).GetValue(form)==null&&main.GetField("activeProcess",flags).GetValue(form)==null,"Process fields released");
  }}finally{Environment.SetEnvironmentVariable("EXPORTER_TEST_SLOW_GIT",previous);}
  Environment.SetEnvironmentVariable("EXPORTER_TEST_SLOW_GIT",null);try{using(var form=(Form)Activator.CreateInstance(main,true)){var h=form.Handle;((TextBox)main.GetField("gitPath",flags).GetValue(form)).Text=args[2];((TextBox)main.GetField("output",flags).GetValue(form)).Text=output;var task=(Task<Tuple<int,string>>)main.GetMethod("RunGitAsync",flags).Invoke(form,new object[]{"status --porcelain",false});Wait(task);Check(task.Result.Item1==0&&task.Result.Item2=="","Successful status uses stdout only");Check(((TextBox)main.GetField("logBox",flags).GetValue(form)).Text.Contains("unknown useful warning"),"Unknown stderr warning stays visible");}}finally{Environment.SetEnvironmentVariable("EXPORTER_TEST_SLOW_GIT",previous);}
  Console.WriteLine("PASS: Git cancellation target; Designer already exited; child termination; remaining queue skipped; resources released; stderr warning not mistaken for status changes.");
 }
}
