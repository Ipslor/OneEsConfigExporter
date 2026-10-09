using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Collections.Specialized;

namespace OneCConfigExporter {
 internal static class ExecutionSafety {
  public const string LockName=".onecexporter.lock";
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder path,uint size,uint flags);
  public static string CanonicalDirectory(string path){string full=Path.GetFullPath(path.Trim());var dir=new DirectoryInfo(full);var missing=new System.Collections.Generic.Stack<string>();while(!dir.Exists&&dir.Parent!=null){missing.Push(dir.Name);dir=dir.Parent;}if(!dir.Exists)throw new IOException("Не найден доступный родитель каталога: "+path);if(dir.Exists){using(var handle=CreateFile(dir.FullName,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero)){
   if(handle.IsInvalid)throw new IOException("Не удалось проверить путь: "+dir.FullName);var buffer=new StringBuilder(32768);uint length=GetFinalPathNameByHandle(handle,buffer,(uint)buffer.Capacity,0);if(length==0||length>=buffer.Capacity)throw new IOException("Не удалось получить окончательный путь каталога.");full=buffer.ToString();if(full.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase))full=@"\\"+full.Substring(8);else if(full.StartsWith(@"\\?\",StringComparison.Ordinal))full=full.Substring(4);
  }}while(missing.Count>0)full=Path.Combine(full,missing.Pop());return full;}
  public static string DirectoryPath(string path){
   if(String.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path)||path.IndexOfAny(new[]{'\r','\n','\0','"'})>=0)throw new IOException("Укажите абсолютный путь к каталогу без кавычек и переносов строк.");
   // C:relative is rooted according to .NET but is not an absolute path.
   if(path.Length>=2&&path[1]==':'&&(path.Length<3||(path[2]!='\\'&&path[2]!='/')))throw new IOException("Нужен абсолютный путь, например C:\\Export, а не C:Export.");
   string full=Path.GetFullPath(path.Trim());var info=new DirectoryInfo(full);if(info.Parent==null)throw new IOException("Корень диска или сетевого ресурса нельзя использовать для выгрузки. Выберите отдельную папку.");
   for(var current=info;current!=null;current=current.Parent){if(current.Name.Equals(".git",StringComparison.OrdinalIgnoreCase))throw new IOException("Нельзя выгружать конфигурацию внутрь служебного каталога .git.");if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Каталог или его родитель является ссылкой/junction. Выберите обычный каталог.");}
   if(File.Exists(full))throw new IOException("Указан файл вместо каталога.");return CanonicalDirectory(full);
  }
  public static void NoLinks(string root,CancellationToken cancellation){
   var pending=new System.Collections.Generic.Stack<DirectoryInfo>();pending.Push(new DirectoryInfo(DirectoryPath(root)));int count=0;
   while(pending.Count>0){cancellation.ThrowIfCancellationRequested();var dir=pending.Pop();if(!dir.Exists)continue;foreach(var entry in dir.EnumerateFileSystemInfos()){
    cancellation.ThrowIfCancellationRequested();
    if((entry.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("В каталоге выгрузки обнаружена ссылка: "+entry.FullName+". Выгрузка остановлена для защиты данных.");
    if(entry is FileInfo&&entry.Name.Equals(LockName,StringComparison.OrdinalIgnoreCase)&&!entry.FullName.Equals(Path.Combine(root,LockName),StringComparison.OrdinalIgnoreCase))CheckFreeLock(entry.FullName);
    if(++count>600000)throw new IOException("Каталог содержит слишком много файлов. Используйте отдельный каталог выгрузки.");
    var child=entry as DirectoryInfo;if(child!=null&&!child.Name.Equals(".git",StringComparison.OrdinalIgnoreCase))pending.Push(child);
   }}
  }
  public static byte[] ReadBounded(string path,int limit){using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
   if(stream.Length>limit)throw new InvalidDataException("Файл слишком велик: "+path);byte[] data=new byte[(int)stream.Length];int position=0,n;while(position<data.Length&&(n=stream.Read(data,position,data.Length-position))>0)position+=n;if(position!=data.Length)throw new EndOfStreamException("Файл изменился при чтении.");return data;
  }}
  public static void CheckFreeLock(string path){try{using(var probe=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){};}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}catch(IOException ex){throw new IOException("Пересекающийся каталог занят другой выгрузкой: "+path,ex);}}
  public static byte[] Utf8WithBom(string text){var encoding=new UTF8Encoding(true);byte[] preamble=encoding.GetPreamble(),body=encoding.GetBytes(text),result=new byte[preamble.Length+body.Length];Buffer.BlockCopy(preamble,0,result,0,preamble.Length);Buffer.BlockCopy(body,0,result,preamble.Length,body.Length);return result;}
  public static void AtomicWrite(string path,byte[] data){path=Path.GetFullPath(path);string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(data,0,data.Length);stream.Flush(true);}if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{try{File.Delete(temporary);}catch{}}}
  public static void CleanGitEnvironment(ProcessStartInfo start){
   StringDictionary environment;
   try{environment=start.EnvironmentVariables;}
   catch(ArgumentException){
    // Framework initializes this dictionary before copying the environment. A case-only
    // duplicate (PATH/Path) leaves it partially populated; rebuild using assignment, not Add.
    environment=start.EnvironmentVariables;environment.Clear();foreach(DictionaryEntry item in Environment.GetEnvironmentVariables()){string key=(string)item.Key;if(!key.StartsWith("=",StringComparison.Ordinal))environment[key]=Environment.GetEnvironmentVariable(key)??Convert.ToString(item.Value);}
   }
   var remove=new System.Collections.Generic.List<string>();foreach(DictionaryEntry item in environment){string key=(string)item.Key;if(key.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase)||key.StartsWith("GCM_",StringComparison.OrdinalIgnoreCase))remove.Add(key);}foreach(string key in remove)environment.Remove(key);
   environment["GIT_TERMINAL_PROMPT"]="0";environment["GCM_INTERACTIVE"]="Never";environment["GIT_TRACE_REDACT"]="1";
  }
 }
 internal sealed class OutputLease : IDisposable {
  readonly FileStream stream;
  OutputLease(FileStream stream){this.stream=stream;}
  public static OutputLease Acquire(string folder){return AcquireCore(folder,CancellationToken.None);}
  public static OutputLease AcquireCore(string folder,CancellationToken cancellation){folder=ExecutionSafety.DirectoryPath(folder);Directory.CreateDirectory(folder);cancellation.ThrowIfCancellationRequested();string path=Path.Combine(folder,ExecutionSafety.LockName);if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Файл блокировки является ссылкой.");
   // Do not delete the lock on release: deletion would introduce a race between open handles.
   try{var stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);try{
     // Recheck overlap after acquiring our lock: two simultaneous parent/child attempts
     // cannot both pass this check while holding their own exclusive handles.
     for(var parent=new DirectoryInfo(folder).Parent;parent!=null;parent=parent.Parent){cancellation.ThrowIfCancellationRequested();ExecutionSafety.CheckFreeLock(Path.Combine(parent.FullName,ExecutionSafety.LockName));}
     ExecutionSafety.NoLinks(folder,cancellation);return new OutputLease(stream);
    }catch{stream.Dispose();throw;}}
   catch(IOException ex){throw new IOException("Каталог занят другой выгрузкой либо файл блокировки недоступен: "+folder,ex);}
  }
  public void Dispose(){stream.Dispose();}
 }
}
