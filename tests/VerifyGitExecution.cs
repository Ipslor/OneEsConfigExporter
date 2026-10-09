using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OneCConfigExporter;
class VerifyGitExecution {
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static Tuple<int,string> Run(Type type,Form form,string command){var task=(Task<Tuple<int,string>>)type.GetMethod("RunGitAsync",flags).Invoke(form,new object[]{command,false});var end=DateTime.UtcNow.AddSeconds(15);while(!task.IsCompleted&&DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(10);}if(!task.IsCompleted)throw new Exception("Git test timeout");return task.GetAwaiter().GetResult();}
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 [STAThread] static void Main(string[] args){
  string root=Path.Combine(args[0],"git-execution-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var type=typeof(UserSettings).Assembly.GetType("OneCConfigExporter.MainForm");
  using(var form=(Form)Activator.CreateInstance(type,true)){var handle=form.Handle;((TextBox)type.GetField("gitPath",flags).GetValue(form)).Text=args[1];((TextBox)type.GetField("output",flags).GetValue(form)).Text=root;
   foreach(string command in new[]{"init --initial-branch=main","config user.name Test","config user.email test@example.invalid","config core.autocrlf true","config core.safecrlf warn"})Check(Run(type,form,command).Item1==0,"Setup: "+command);
   for(int i=0;i<5;i++)File.WriteAllText(Path.Combine(root,"file"+i+".xml"),"<test>\nhello\n</test>\n",new UTF8Encoding(false));
   var added=Run(type,form,"add -A");Check(added.Item1==0,"Add succeeded");var log=(TextBox)type.GetField("logBox",flags).GetValue(form);Check(log.Text.Contains("предупреждения о переносах строк LF/CRLF — 5")&&!log.Text.Contains("LF will be replaced"),"Real Git warnings compacted");
   Check(Run(type,form,"commit -m test").Item1==0,"Commit succeeded");Check(log.Text.Contains("свёрнут — 5")&&!log.Text.Contains("create mode"),"Real commit file listing compacted");
   Check(String.IsNullOrWhiteSpace(Run(type,form,"status --porcelain -- .").Item2),"Clean status result preserved");
   var failed=Run(type,form,"unknown-test-command");Check(failed.Item1!=0&&failed.Item2.Contains("not a git command")&&log.Text.Contains("not a git command"),"Real Git failure stays visible and nonzero");
  }Console.WriteLine("PASS: actual Git add/commit/status; five LF warnings compacted; clean-status semantics; error exit and message preserved.");
 }
}
