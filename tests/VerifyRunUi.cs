using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using OneCConfigExporter;
class VerifyRunUi {
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 [STAThread] static void Main(string[] args){
  var type=typeof(UserSettings).Assembly.GetType("OneCConfigExporter.MainForm");string root=Path.Combine(args[0],"ui-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  using(var form=(Form)Activator.CreateInstance(type,true)){
   var handle=form.Handle;var rows=new List<BatchBase>();foreach(string name in new[]{"fail","one","two"})rows.Add(new BatchBase{Name=name,FileMode=false,Server="test",Database=name,Output=Path.Combine(root,name),DbPassword="test-secret",RepoPassword="storage-secret"});
   type.GetField("batchBases",flags).SetValue(form,rows);((ComboBox)type.GetField("platform",flags).GetValue(form)).Text=args[1];type.GetMethod("ReloadBatchRows",flags).Invoke(form,null);
   var grid=(DataGridView)type.GetField("batchGrid",flags).GetValue(form);var tabs=(TabControl)type.GetField("logTabs",flags).GetValue(form);var history=(TextBox)type.GetField("preview",flags).GetValue(form);
   var task=(Task)type.GetMethod("RunBatchAsync",flags).Invoke(form,new object[]{false});bool liveError=false;var end=DateTime.UtcNow.AddSeconds(60);
   while(!task.IsCompleted&&DateTime.UtcNow<end){Application.DoEvents();if((bool)type.GetField("batchRunning",flags).GetValue(form)){Check(grid.Enabled&&grid.ReadOnly,"Grid browsable but read-only during run");if(Convert.ToString(grid.Rows[0].Cells["Статус"].Value).Contains("Ошибка")){liveError=true;Check(Convert.ToString(grid.Rows[0].Cells["Пояснение"].Value).Contains("Тестовый отказ"),"Full error available before completion");tabs.SelectedIndex=0;}}Thread.Sleep(10);}
   Check(task.IsCompleted,"Queue timeout");task.GetAwaiter().GetResult();Check(liveError,"Error inspected while queue running");string completed=(string)type.GetMethod("BatchDetailsText",flags).Invoke(form,new object[]{grid.Rows[1]});Check(completed.Contains("Конфигурация выгружена в файлы")&&completed.Contains("Выгрузка проверена")&&!completed.Contains("Создан коммит"),"Completed steps without unrequested Git");Check(grid.Enabled&&!grid.ReadOnly,"Grid editable after completion");
   Check(Regex.Matches(history.Text," DESIGNER").Count==3,"Three launch commands retained");foreach(string name in new[]{"fail","one","two"})Check(history.Text.Contains("=== "+name+" — "),"Command base label "+name);
   Check(!history.Text.Contains("test-secret")&&!history.Text.Contains("storage-secret"),"Passwords masked in all commands");string before=history.Text;((TextBox)type.GetField("server",flags).GetValue(form)).Text="changed";Check(history.Text==before,"Input updates do not replace command history");Check(tabs.SelectedIndex==0,"Selected command tab preserved");
  }Console.WriteLine("PASS: live read-only grid; early full error reason; three labelled commands; masked passwords; stable history and tab selection.");
 }
}
