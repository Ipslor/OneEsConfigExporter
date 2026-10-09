using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using OneCConfigExporter;
class VerifyGitLog {
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 static object Read(Type type,string input,string file,bool commit){using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(input)))using(var reader=new StreamReader(stream,Encoding.UTF8)){
  var task=(Task)type.GetMethod("ReadAsync").Invoke(null,new object[]{reader,file,"stderr",new[]{"test-secret"},commit});task.GetAwaiter().GetResult();return task.GetType().GetProperty("Result").GetValue(task,null);
 }}
 static string Text(object item){return (string)item.GetType().GetField("Text").GetValue(item);}
 static void Main(string[] args){
  var type=typeof(UserSettings).Assembly.GetType("OneCConfigExporter.GitLog");string root=Path.Combine(args[0],"git-log-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string file=Path.Combine(root,"details.log");var input=new StringBuilder();for(int i=0;i<5000;i++)input.AppendLine("warning: in the working copy of 'Reports/test"+i+".xml', LF will be replaced by CRLF the next time Git touches it");input.AppendLine("warning: important unknown warning");input.AppendLine("fatal: authentication failed test-secret");
  var result=Read(type,input.ToString(),file,false);Check((long)result.GetType().GetField("LineEndings").GetValue(result)==5000,"Exact warning count");Check(Text(result).Contains("fatal: authentication failed")&&Text(result).Contains("important unknown warning")&&!Text(result).Contains("LF will be replaced")&&!Text(result).Contains("test-secret"),"Real late error and unknown warning retained, secret masked");string raw=File.ReadAllText(file);Check(raw.Contains("Reports/test4999.xml")&&!raw.Contains("test-secret"),"Raw warning details retained and masked");
  var empty=Read(type,"",file,false);string summary=(string)type.GetMethod("Summary").Invoke(null,new object[]{empty,result,false});Check(summary.Contains("5000")&&summary.Length<1000,"Compact warning summary");
  var commit=Read(type,"[main abc] Update\n 2 files changed\n create mode 100644 test.xml\n delete mode 100644 old.xml\n",file,true);Check((long)commit.GetType().GetField("FileDetails").GetValue(commit)==2&&Text(commit).Contains("2 files changed")&&!Text(commit).Contains("create mode"),"Commit paths compacted, totals kept");
  var status=Read(type," M file.xml\n?? other.xml\n",file,false);summary=(string)type.GetMethod("Summary").Invoke(null,new object[]{status,empty,true});Check(summary.Contains("2.")&&!summary.Contains("file.xml"),"Status count without path noise");
  var failed=Read(type,"fatal: invalid operation\n",Path.Combine(root,"missing","file.log"),false);Check(Text(failed).Contains("fatal")&&((string)failed.GetType().GetField("LogError").GetValue(failed)).Length>0,"Disk failure does not hide Git error");
  Console.WriteLine("PASS: 5000 warnings compacted; late error/unknown warning retained; raw details and masking; commit totals; status count; disk-log failure.");
 }
}
