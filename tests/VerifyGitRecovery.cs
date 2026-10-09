using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OneCConfigExporter;

class VerifyGitRecovery {
 static Type main,recovery;static string git,root;static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static string Field(object value,string name){return (string)recovery.GetField(name).GetValue(value);}
 static object Advice(string folder,string error){return recovery.GetMethod("Create").Invoke(null,new object[]{git,folder,error});}
 static Tuple<int,string> ProcessRun(string exe,string arguments){using(var p=Process.Start(new ProcessStartInfo{FileName=exe,Arguments=arguments,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8})){
  var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();if(!p.WaitForExit(15000)){p.Kill();throw new Exception("Process timeout");}Task.WaitAll(stdout,stderr);return Tuple.Create(p.ExitCode,stdout.Result+stderr.Result);
 }}
 static string Git(string folder,string arguments){var result=ProcessRun(git,"-c core.quotepath=false -C \""+folder+"\" "+arguments);Check(result.Item1==0,"Git fixture: "+arguments+"\n"+result.Item2);return result.Item2;}
 static Tuple<int,string> PowerShell(string command){return ProcessRun(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell\\v1.0\\powershell.exe"),"-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(command+"; exit $LASTEXITCODE")));}
 static IEnumerable<Control> Descendants(Control parent){foreach(Control c in parent.Controls){yield return c;foreach(var child in Descendants(c))yield return child;}}
 static void Wait(Task task){var end=DateTime.UtcNow.AddSeconds(30);while(!task.IsCompleted&&DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(10);}Check(task.IsCompleted,"UI task timeout");task.GetAwaiter().GetResult();}
 [STAThread]static void Main(string[] args){
  var assembly=typeof(UserSettings).Assembly;main=assembly.GetType("OneCConfigExporter.MainForm");recovery=assembly.GetType("OneCConfigExporter.GitRecovery");git=args[2];root=Path.Combine(args[0],"git-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string repo=Path.Combine(root,"repo's $() &; база"),folder=Path.Combine(repo,"export's $() &; файлы");Directory.CreateDirectory(folder);Git(repo,"init --initial-branch=main");Git(repo,"config user.name Test");Git(repo,"config user.email test@example.invalid");
  string target=Path.Combine(folder,"pending.txt"),outside=Path.Combine(repo,"outside.txt"),log=Path.Combine(folder,"1c-dump.log"),lease=Path.Combine(folder,".onecexporter.lock");File.WriteAllText(target,"original");File.WriteAllText(outside,"outside");File.WriteAllText(log,"log");File.WriteAllText(lease,"lock");Git(repo,"add -A");
  string indexPath=Path.Combine(repo,".git","index");byte[] index=File.ReadAllBytes(indexPath);var advice=Advice(folder,"В каталоге уже есть подготовленные к коммиту изменения.");
  Check(index.SequenceEqual(File.ReadAllBytes(indexPath)),"Generating advice does not mutate index");Check(Field(advice,"Text").Contains("ВСЕ")&&Field(advice,"Text").Contains("PowerShell"),"Commit scope and shell explained");Check(Field(advice,"UnstageCommand").Contains("''")&&!Field(advice,"UnstageCommand").Contains("--force"),"Quoted path and no forced action");
  using(var form=(Form)Activator.CreateInstance(main,true)){
   var handle=form.Handle;((TextBox)main.GetField("gitPath",flags).GetValue(form)).Text=git;((TextBox)main.GetField("output",flags).GetValue(form)).Text=folder;
   var profile=new BatchBase{Name="Recovery",Output=folder,Commit=true};main.GetField("batchBases",flags).SetValue(form,new List<BatchBase>{profile});main.GetMethod("ReloadBatchRows",flags).Invoke(form,null);
   var preflight=(Task<string>)main.GetMethod("GitPreflight",flags).Invoke(form,null);Wait(preflight);Check(preflight.Result.Contains("подготовленные"),"Pending index detected");main.GetField("activeBatchBase",flags).SetValue(form,profile);main.GetMethod("CurrentOperation",flags).Invoke(form,new object[]{"Проверка и подготовка локального Git-репозитория"});main.GetMethod("SetBatchResult",flags).Invoke(form,new object[]{profile,"✕ Ошибка",preflight.Result,Color.Red});
   var grid=(DataGridView)main.GetField("batchGrid",flags).GetValue(form);string details=(string)main.GetMethod("BatchDetailsText",flags).Invoke(form,new object[]{grid.Rows[0]});Check(details.Contains("restore --staged")&&details.Contains("rm -r --cached")&&details.Contains(folder.Replace("'","''")),"Recovery appears in result for the correct row");
   string tooltip=(string)main.GetMethod("BatchTooltipText",flags).Invoke(form,new object[]{grid.Rows[0]});Check(tooltip.Contains("Двойной щелчок")&&!tooltip.Contains("restore --staged")&&tooltip.Length<=1800,"Tooltip remains compact and points to commands dialog");
   Exception dialogError=null;bool inspected=false;using(var timer=new System.Windows.Forms.Timer{Interval=200}){timer.Tick+=(s,e)=>{var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text.StartsWith("Результат обработки"));if(dialog==null)return;timer.Stop();try{dialog.ClientSize=new Size(500,300);dialog.PerformLayout();var buttons=Descendants(dialog).OfType<Button>().Where(c=>c.Visible).ToArray();Check(buttons.Any(c=>c.Text=="Копировать снятие с индекса")&&buttons.Any(c=>c.Text=="Копировать команду коммита"),"Recovery copy buttons present");var bounds=dialog.RectangleToScreen(dialog.ClientRectangle);foreach(var button in buttons)Check(bounds.Contains(button.RectangleToScreen(button.ClientRectangle)),"Copy button fits compact window: "+button.Text);using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(root,"recovery.png"));}inspected=true;}catch(Exception ex){dialogError=ex;}finally{dialog.Close();}};timer.Start();main.GetMethod("ShowBatchDetails",flags).Invoke(form,new object[]{grid.Rows[0]});}if(dialogError!=null)throw dialogError;Check(inspected,"Details dialog inspected");
   main.GetMethod("CurrentOperation",flags).Invoke(form,new object[]{"Выгрузка конфигурации"});grid.Rows[0].Cells["Пояснение"].Value="Ошибка 1С";details=(string)main.GetMethod("BatchDetailsText",flags).Invoke(form,new object[]{grid.Rows[0]});Check(!details.Contains("Как продолжить"),"No Git advice for a 1C error");
  }
  File.WriteAllText(target,"unstaged newer version");var refused=PowerShell(Field(advice,"UnstageCommand"));Check(refused.Item1!=0&&File.ReadAllText(target)=="unstaged newer version"&&Git(repo,"diff --cached --name-only").Contains("pending.txt"),"Unborn staged-only content not forcibly discarded");
  File.WriteAllText(target,"original");var result=PowerShell(Field(advice,"UnstageCommand"));Check(result.Item1==0,"Unborn recovery: "+result.Item2);string staged=Git(repo,"diff --cached --name-only");Check(!staged.Contains("pending.txt")&&staged.Contains("outside.txt")&&staged.Contains("1c-dump.log")&&staged.Contains(".onecexporter.lock"),"Unborn unstage is scoped and excludes service files");Check(File.ReadAllText(target)=="original","Unborn recovery keeps file bytes");
  Git(repo,"add -A");result=PowerShell(Field(advice,"CommitCommand"));Check(result.Item1==0,"Commit recovery: "+result.Item2);File.WriteAllText(target,"new content");File.WriteAllText(outside,"outside new");File.WriteAllText(log,"log new");Git(repo,"add -A");File.WriteAllText(target,"working version differs");refused=PowerShell(Field(advice,"UnstageCommand"));Check(refused.Item1!=0&&Git(repo,"diff --cached --name-only").Contains("pending.txt")&&File.ReadAllText(target)=="working version differs","Existing HEAD index-only snapshot preserved");File.WriteAllText(target,"new content");result=PowerShell(Field(advice,"UnstageCommand"));Check(result.Item1==0,"Existing HEAD recovery: "+result.Item2);staged=Git(repo,"diff --cached --name-only");Check(!staged.Contains("pending.txt")&&staged.Contains("outside.txt")&&staged.Contains("1c-dump.log"),"Existing HEAD recovery only affects export scope");Check(File.ReadAllText(target)=="new content","Tracked working file unchanged");
  result=PowerShell(Field(advice,"CommitCommand"));Check(result.Item1==0&&Git(repo,"show HEAD:outside.txt").Trim()=="outside new","Commit saves staged files");Check(Git(repo,"show \"HEAD:export's $() &; файлы/pending.txt\"").Trim()=="original","Commit does not include unstaged export file");
  foreach(string error in new[]{"Git занят (index.lock)","Git находится в незавершённой операции (MERGE_HEAD)","В репозитории есть неразрешённые конфликты.","fatal: dubious ownership"}){var other=Advice(folder,error);Check(Field(other,"CheckCommand").Length>0&&Field(other,"UnstageCommand").Length==0&&Field(other,"CommitCommand").Length==0,"No staged recovery for other error");}
  Check(Field(Advice("relative","подготовленные к коммиту"),"Text")=="","Invalid folder does not produce commands");
  var settings=assembly.GetType("OneCConfigExporter.AppSettingsForm");using(var form=(Form)Activator.CreateInstance(settings,new object[]{"",new string[0],git,"",""})){
   form.Show();Application.DoEvents();form.PerformLayout();var links=Descendants(form).OfType<LinkLabel>().ToArray();Check(links.Length==2&&links.Any(l=>Convert.ToString(l.Tag)=="https://git-scm.com/install/windows")&&links.Any(l=>Convert.ToString(l.Tag)=="https://github.com/"),"Official links in settings");foreach(var c in Descendants(form).Where(c=>c is LinkLabel||c is Button))Check(form.RectangleToScreen(form.ClientRectangle).Contains(c.RectangleToScreen(c.ClientRectangle)),"Settings control fits: "+c.Text);
   using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(root,"settings.png"));}
  }
  Console.WriteLine("PASS: safe PowerShell commands; unborn/existing HEAD; no forced loss; scoped index; preserved working files; commit semantics; row guidance; settings links.");
 }
}
