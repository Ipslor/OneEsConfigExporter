using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Forms;
using OneCConfigExporter;
class VerifyStages {
 static Form current;
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool value,string text){if(!value)throw new Exception(text);}
 static void Wait(Task task){var end=DateTime.UtcNow.AddSeconds(90);while(!task.IsCompleted&&DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(10);}if(!task.IsCompleted&&current!=null){var t=current.GetType();Console.WriteLine("STATUS="+((Label)t.GetField("status",flags).GetValue(current)).Text);Console.WriteLine("LOG="+((TextBox)t.GetField("logBox",flags).GetValue(current)).Text);var p=t.GetField("activeGitProcess",flags).GetValue(current) as System.Diagnostics.Process;Console.WriteLine("GIT="+(p==null?"null":p.HasExited.ToString()));}Check(task.IsCompleted,"Timeout");task.GetAwaiter().GetResult();}
 [STAThread] static void Main(string[] args){
  string root=Path.Combine(args[0],"stages-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var type=typeof(UserSettings).Assembly.GetType("OneCConfigExporter.MainForm");
  using(var form=(Form)Activator.CreateInstance(type,true)){
   current=form;Action<Control> create=null;create=c=>{var h=c.Handle;foreach(Control child in c.Controls)create(child);};create(form);var profile=new BatchBase{Name="Stage test",FileMode=false,Server="test",Database="one",Output=root,Commit=true};type.GetField("batchBases",flags).SetValue(form,new List<BatchBase>{profile});((ComboBox)type.GetField("platform",flags).GetValue(form)).Text=args[1];((TextBox)type.GetField("gitPath",flags).GetValue(form)).Text=args[2];type.GetField("gitReady",flags).SetValue(form,true);type.GetMethod("SelectBatchProfile",flags).Invoke(form,new object[]{profile});type.GetMethod("ReloadBatchRows",flags).Invoke(form,null);
   foreach(string command in new[]{"init --initial-branch=main","config user.name Test","config user.email test@example.invalid"}){var task=(Task<Tuple<int,string>>)type.GetMethod("RunGitAsync",flags).Invoke(form,new object[]{command,false});Wait(task);Check(task.Result.Item1==0,"Git fixture");}
   Wait((Task)type.GetMethod("RunBatchAsync",flags).Invoke(form,new object[]{false}));var grid=(DataGridView)type.GetField("batchGrid",flags).GetValue(form);string summary=(string)type.GetMethod("BatchDetailsText",flags).Invoke(form,new object[]{grid.Rows[0]});Check(summary.Contains("Конфигурация выгружена в файлы")&&summary.Contains("Выгрузка проверена")&&summary.Contains("Создан коммит в локальном Git")&&!summary.Contains("отправлены на GitHub"),"Only completed export/check/commit shown");
   profile.Incremental=true;Wait((Task)type.GetMethod("RunBatchAsync",flags).Invoke(form,new object[]{false}));summary=(string)type.GetMethod("BatchDetailsText",flags).Invoke(form,new object[]{grid.Rows[0]});Check(summary.Contains("коммит не создавался")&&!summary.Contains("Создан коммит в локальном Git"),"No-change run resets summary and does not claim a commit");
   profile.Database="fail";Wait((Task)type.GetMethod("RunBatchAsync",flags).Invoke(form,new object[]{false}));summary=(string)type.GetMethod("BatchDetailsText",flags).Invoke(form,new object[]{grid.Rows[0]});Check(summary.Contains("Тестовый отказ")&&!summary.Contains("Конфигурация выгружена в файлы")&&!summary.Contains("Выгрузка проверена"),"Failed export has no completed steps");
  }
  string broken=Path.Combine(root,"broken");Directory.CreateDirectory(broken);File.WriteAllText(Path.Combine(broken,"Configuration.xml"),"<Configuration />");bool rejected=false;try{type.GetMethod("CheckExportFiles",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{broken});}catch(TargetInvocationException ex){rejected=ex.InnerException is IOException;}Check(rejected,"Missing dump metadata rejected");
  using(var stream=typeof(UserSettings).Assembly.GetManifestResourceStream("OneCConfigExporter.UsageGuide.md"))using(var reader=new StreamReader(stream)){Check(!reader.ReadToEnd().Contains("работает только со списком"),"Quick start updated");}
  Console.WriteLine("PASS: export/check/real commit summary; no-change distinction; reset per run; failure summary; missing XML validation; quick-start wording.");
 }
}
