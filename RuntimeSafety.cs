using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace OneCConfigExporter {
 internal static class RuntimeSafety {
  public const int TextLimit=256*1024;
  public static string Tail(string text){return text.Length<=TextLimit?text:"[Показан конец журнала]\r\n"+text.Substring(text.Length-TextLimit);}
  // Drain both process pipes even after the retained text limit is reached.
  public static async Task<string> ReadPipe(StreamReader reader){var retained=new StringBuilder();var buffer=new char[4096];int count;bool truncated=false;while((count=await reader.ReadAsync(buffer,0,buffer.Length).ConfigureAwait(false))>0){int take=Math.Min(count,TextLimit-retained.Length);retained.Append(buffer,0,take);if(take<count)truncated=true;}return retained.ToString()+(truncated?"\r\n[Ответ процесса сокращён]\r\n":"");}
  public static string ReadLog(string path){return ReadLogCore(path,true);}
  public static string ReadLogCore(string path,bool complete){using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
   int first=stream.ReadByte(),second=stream.ReadByte();Encoding encoding=first==255&&second==254?Encoding.Unicode:first==254&&second==255?Encoding.BigEndianUnicode:null;
   if(encoding==null){stream.Position=0;byte[] sample=new byte[4096];int sampleSize=stream.Read(sample,0,sample.Length);try{new UTF8Encoding(false,true).GetDecoder().GetCharCount(sample,0,sampleSize,false);encoding=Encoding.UTF8;}catch(DecoderFallbackException){encoding=Encoding.GetEncoding(1251);}}
   long length=stream.Length;long start=Math.Max(0,length-TextLimit);if((encoding==Encoding.Unicode||encoding==Encoding.BigEndianUnicode)&&start%2!=0)start++;stream.Position=start;byte[] bytes=new byte[(int)Math.Min(TextLimit,Math.Max(0,length-start))];int read=0,n;while(read<bytes.Length&&(n=stream.Read(bytes,read,bytes.Length-read))>0)read+=n;
   string text=encoding.GetString(bytes,0,read);if(start>0){int newline=text.IndexOf('\n');text=newline<0?"[Слишком длинная строка журнала не показана]":text.Substring(newline+1);}if(!complete&&!text.EndsWith("\n",StringComparison.Ordinal)){int newline=text.LastIndexOf('\n');text=newline<0?"":text.Substring(0,newline+1);}
   return (start>0?"[Показан конец файла журнала]\r\n":"")+text.TrimStart('\uFEFF');
  }}
  static readonly object LogLock=new object();
  public static void AppendEvent(string path,string entry){lock(LogLock){if(File.Exists(path)&&new FileInfo(path).Length>10*1024*1024){string backup=path+".previous";if(File.Exists(backup))File.Delete(backup);File.Move(path,backup);}File.AppendAllText(path,entry,Encoding.UTF8);}}
  public static void KillTree(Process process){try{if(process.HasExited)return;using(var killer=Process.Start(new ProcessStartInfo{FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"taskkill.exe"),Arguments="/PID "+process.Id+" /T /F",UseShellExecute=false,CreateNoWindow=true})){killer.WaitForExit(5000);}}catch{}try{if(!process.HasExited)process.Kill();}catch{}}
  public static bool ContainsPath(string parent,string child){parent=Path.GetFullPath(parent).TrimEnd('\\')+"\\";child=Path.GetFullPath(child).TrimEnd('\\')+"\\";return child.StartsWith(parent,StringComparison.OrdinalIgnoreCase);}
 }
}
