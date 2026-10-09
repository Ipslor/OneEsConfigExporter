using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using OneCConfigExporter;
class VerifyReview {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 [STAThread] static void Main(string[] args){
  var assembly=typeof(UserSettings).Assembly;var safety=assembly.GetType("OneCConfigExporter.RuntimeSafety");var settings=assembly.GetType("OneCConfigExporter.SettingsStore");string root=Path.Combine(args[0],"review-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  foreach(var encoding in new Encoding[]{Encoding.UTF8,Encoding.Unicode,Encoding.BigEndianUnicode,Encoding.GetEncoding(1251)}){
   string file=Path.Combine(root,"log-"+encoding.CodePage);using(var writer=new StreamWriter(file,false,encoding)){for(int i=0;i<10000;i++)writer.WriteLine("Тестовый журнал длинной операции: строка "+i);writer.Write("КОНЕЦ");}
   string tail=(string)safety.GetMethod("ReadLog").Invoke(null,new object[]{file});Check(tail.Length<270000&&tail.EndsWith("КОНЕЦ"),"Bounded log and encoding "+encoding.CodePage);
  }
  Check((bool)safety.GetMethod("ContainsPath").Invoke(null,new object[]{root,Path.Combine(root,"child")}),"Nested paths detected");
  Check(!(bool)safety.GetMethod("ContainsPath").Invoke(null,new object[]{root,root+"-other"}),"Sibling prefix not confused");
  string oversized=Path.Combine(root,"oversized.dat");using(var stream=File.Create(oversized))stream.SetLength(5*1024*1024);bool rejected=false;try{settings.GetMethod("Load").Invoke(null,new object[]{oversized});}catch(TargetInvocationException ex){rejected=ex.InnerException is InvalidDataException;}Check(rejected,"Oversized settings rejected before DPAPI");
  var invalid=new UserSettings();invalid.BatchBases.Add(null);rejected=false;try{settings.GetMethod("Save").Invoke(null,new object[]{Path.Combine(root,"invalid.dat"),invalid});}catch(TargetInvocationException ex){rejected=ex.InnerException is InvalidDataException;}Check(rejected,"Null rows rejected");
  var main=assembly.GetType("OneCConfigExporter.MainForm");using(var form=(Form)Activator.CreateInstance(main,true)){var flags=BindingFlags.Instance|BindingFlags.NonPublic;var button=(Button)main.GetField("scenarioButton",flags).GetValue(form);Check(button.Text=="Сохранить как сценарий","Button renamed");((TextBox)main.GetField("output",flags).GetValue(form)).Text="bad\"path";Check(((TextBox)main.GetField("preview",flags).GetValue(form)).Text.Length>0,"Invalid input does not crash preview");}
  using(var stream=assembly.GetManifestResourceStream("OneCConfigExporter.UsageGuide.md"))using(var reader=new StreamReader(stream)){string guide=reader.ReadToEnd();Check(!guide.Contains("одиноч")&&guide.Contains("Сохранить как сценарий"),"Embedded help updated");}
  string blocked=Path.Combine(root,"blocked.ps1");Directory.CreateDirectory(blocked);string snapshot=Path.Combine(root,"blocked.settings.dat");settings.GetMethod("Save").Invoke(null,new object[]{snapshot,new UserSettings{CommitMessage="Previous"}});string previous=Convert.ToBase64String(File.ReadAllBytes(snapshot));try{assembly.GetType("OneCConfigExporter.AutomaticScenario").GetMethod("Write").Invoke(null,new object[]{blocked,assembly.Location,new UserSettings{CommitMessage="Changed"}});}catch(TargetInvocationException){}Check(previous==Convert.ToBase64String(File.ReadAllBytes(snapshot)),"Scenario write failure restores previous snapshot");
  Console.WriteLine("PASS: bounded logs UTF8/UTF16/CP1251; overlapping paths; oversized/null settings; invalid preview input; renamed button; embedded help; scenario snapshot rollback.");
 }
}
