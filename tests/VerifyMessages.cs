using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using OneCConfigExporter;
class VerifyMessages {
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool value,string text){if(!value)throw new Exception(text);}
 [STAThread] static void Main(string[] args){
  string root=Path.Combine(args[0],"messages-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var asm=typeof(UserSettings).Assembly;var editor=asm.GetType("OneCConfigExporter.BatchBaseForm");var profile=new BatchBase{Name="Test",FileMode=false,Server="server",Database="db",Output=root};
  using(var form=(Form)Activator.CreateInstance(editor,new object[]{profile,new object[0],true,true})){
   var text=(TextBox)editor.GetField("message",flags).GetValue(form);var full=(RadioButton)editor.GetField("full",flags).GetValue(form);var incremental=(RadioButton)editor.GetField("incremental",flags).GetValue(form);
   Check(text.Text==BatchBase.DefaultCommitMessage(false),"Default full message");text.Text="Custom full";incremental.Checked=true;Check(text.Text==BatchBase.DefaultCommitMessage(true),"Automatic incremental default");text.Text="Custom increment";full.Checked=true;Check(text.Text=="Custom full","Full custom text restored");incremental.Checked=true;Check(text.Text=="Custom increment","Incremental custom text restored");editor.GetMethod("Save",flags).Invoke(form,null);profile=(BatchBase)editor.GetField("Result").GetValue(form);Check(profile.FullCommitMessage=="Custom full"&&profile.IncrementalCommitMessage=="Custom increment"&&profile.Incremental,"Both messages saved in base");
  }
  var store=asm.GetType("OneCConfigExporter.SettingsStore");var settings=new UserSettings{BatchBases=new List<BatchBase>{profile,new BatchBase{Name="Other",FullCommitMessage="Other full",IncrementalCommitMessage="Other inc"}}};string path=Path.Combine(root,"saved.dat");store.GetMethod("Save").Invoke(null,new object[]{path,settings});var loaded=(UserSettings)store.GetMethod("Load").Invoke(null,new object[]{path});Check(loaded.BatchBases[0].MessageFor(true)=="Custom increment"&&loaded.BatchBases[1].MessageFor(false)=="Other full","Separate bases and modes persisted");
  string script=Path.Combine(root,"run.ps1");string snapshot=(string)asm.GetType("OneCConfigExporter.AutomaticScenario").GetMethod("Write").Invoke(null,new object[]{script,asm.Location,settings});loaded=(UserSettings)store.GetMethod("Load").Invoke(null,new object[]{snapshot});Check(loaded.BatchBases[0].FullCommitMessage=="Custom full"&&loaded.BatchBases[0].IncrementalCommitMessage=="Custom increment","Automatic scenario stores both messages");
  string legacy=Path.Combine(root,"legacy.dat");var json=new JavaScriptSerializer().Serialize(new {Version=1,CommitMessage="Legacy",BatchBases=new[]{new {Name="Old",Enabled=true,FileMode=false,Server="s",Database="d",Output=root}}});File.WriteAllBytes(legacy,ProtectedData.Protect(Encoding.UTF8.GetBytes(json),Encoding.UTF8.GetBytes("OneCConfigExporter/settings/v1"),DataProtectionScope.CurrentUser));loaded=(UserSettings)store.GetMethod("Load").Invoke(null,new object[]{legacy});Check(loaded.BatchBases[0].MessageFor(false)=="Legacy — полная выгрузка"&&loaded.BatchBases[0].MessageFor(true)=="Legacy — инкрементальная выгрузка","Old common custom message migrated per mode");
  var main=asm.GetType("OneCConfigExporter.MainForm");using(var form=(Form)Activator.CreateInstance(main,true)){main.GetMethod("SelectBatchProfile",flags).Invoke(form,new object[]{profile});Check(((TextBox)main.GetField("commitMessage",flags).GetValue(form)).Text=="Custom increment","Engine uses selected base and mode");}
  string broken=Path.Combine(root,"broken");Directory.CreateDirectory(broken);File.WriteAllText(Path.Combine(broken,"Configuration.xml"),"<Configuration><bad></Configuration>");File.WriteAllText(Path.Combine(broken,"ConfigDumpInfo.xml"),"<ConfigDumpInfo />");bool rejected=false;try{main.GetMethod("CheckExportFiles",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{broken});}catch(TargetInvocationException ex){rejected=ex.InnerException is System.Xml.XmlException;}Check(rejected,"Malformed trailing XML rejected");
  Console.WriteLine("PASS: per-base/per-mode messages; editor switching preserves custom texts; DPAPI persistence; scenario snapshot; legacy migration; engine message selection; malformed XML check.");
 }
}
