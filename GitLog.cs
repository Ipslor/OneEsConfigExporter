using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OneCConfigExporter {
 internal sealed class GitLogResult {
  public string Text="",LogError="";
  public long LineEndings,FileDetails,Lines;
 }
 internal static class GitLog {
  static readonly object RawLock=new object();
  public static string Mask(string text,string[] secrets){foreach(string secret in secrets)if(!String.IsNullOrEmpty(secret))text=text.Replace(secret,"••••••");return text;}
  public static void WriteRaw(string path,string text){lock(RawLock)RuntimeSafety.AppendEvent(path,text);}
  static bool IsLineEndingWarning(string line){return line.StartsWith("warning: in the working copy of '",StringComparison.Ordinal)&&
   (line.EndsWith("', LF will be replaced by CRLF the next time Git touches it",StringComparison.Ordinal)||line.EndsWith("', CRLF will be replaced by LF the next time Git touches it",StringComparison.Ordinal));}
  // Compact known noise before applying the memory limit, so a late error survives a warning flood.
  public static async Task<GitLogResult> ReadAsync(StreamReader reader,string path,string channel,string[] secrets,bool commit){
   var result=new GitLogResult();var shown=new StringBuilder();var raw=new StringBuilder();var line=new StringBuilder();var chars=new char[4096];bool oversized=false;
   Action flush=()=>{if(raw.Length==0)return;try{WriteRaw(path,raw.ToString());}catch(Exception ex){result.LogError=ex.Message;}raw.Clear();};
   Action consume=()=>{
    string lineValue=line.ToString().TrimEnd('\r');string value=Mask(lineValue,secrets);line.Clear();result.Lines++;
    if(oversized){value="[Слишком длинная строка скрыта для защиты памяти и секретов]";oversized=false;}
    raw.Append('[').Append(channel).Append("] ").AppendLine(value);if(raw.Length>=16384)flush();
    if(IsLineEndingWarning(lineValue)){result.LineEndings++;return;}
    if(commit&&(value.StartsWith(" create mode ",StringComparison.Ordinal)||value.StartsWith(" delete mode ",StringComparison.Ordinal)||value.StartsWith(" rename ",StringComparison.Ordinal)||value.StartsWith(" copy ",StringComparison.Ordinal))){result.FileDetails++;return;}
    shown.AppendLine(value);if(shown.Length>RuntimeSafety.TextLimit*2){shown.Remove(0,shown.Length-RuntimeSafety.TextLimit);}
   };
   try{int count;while((count=await reader.ReadAsync(chars,0,chars.Length).ConfigureAwait(false))>0)for(int i=0;i<count;i++){if(chars[i]=='\n')consume();else if(line.Length<65536)line.Append(chars[i]);else oversized=true;}if(line.Length>0||oversized)consume();}
   finally{flush();}
   result.Text=RuntimeSafety.Tail(shown.ToString());return result;
  }
  public static string Summary(GitLogResult stdout,GitLogResult stderr,bool status){
   string text=status?"Изменённых/неотслеживаемых путей: "+stdout.Lines+".\r\n"+stderr.Text:stdout.Text+stderr.Text;
   long endings=stdout.LineEndings+stderr.LineEndings,details=stdout.FileDetails+stderr.FileDetails;
   if(endings>0)text+="[Git: предупреждения о переносах строк LF/CRLF — "+endings+". Это не ошибка; подробности в git-details.log.]\r\n";
   if(details>0)text+="[Git: список созданных/удалённых/переименованных файлов свёрнут — "+details+". Подробности в git-details.log.]\r\n";
   if(stdout.LogError.Length>0||stderr.LogError.Length>0)text+="[Не удалось сохранить подробный журнал Git: "+stdout.LogError+" "+stderr.LogError+"]\r\n";
   return RuntimeSafety.Tail(text);
  }
 }
}
