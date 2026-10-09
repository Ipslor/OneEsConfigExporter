using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OneCConfigExporter {
 internal static class ExportSizeFilter {
  internal struct FileStamp {public long Length,Ticks;public FileStamp(FileInfo file){Length=file.Length;Ticks=file.LastWriteTimeUtc.Ticks;}public bool Matches(FileInfo file){return Length==file.Length&&Ticks==file.LastWriteTimeUtc.Ticks;}}
  static IEnumerable<FileInfo> Files(string root,CancellationToken cancellation){
   var directory=new DirectoryInfo(root);if((directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Фильтр размера не применяется к каталогу-ссылке. Выберите обычный каталог.");
   var dirs=new Stack<DirectoryInfo>();dirs.Push(directory);
   int count=0;while(dirs.Count>0){cancellation.ThrowIfCancellationRequested();var dir=dirs.Pop();foreach(var f in dir.EnumerateFiles()){cancellation.ThrowIfCancellationRequested();if(++count>500000)throw new IOException("Слишком много файлов для фильтра размера (максимум 500 000).");if((f.Attributes&FileAttributes.ReparsePoint)==0)yield return f;}
    foreach(var child in dir.EnumerateDirectories()){cancellation.ThrowIfCancellationRequested();if(dirs.Count>=500000)throw new IOException("Слишком много каталогов для фильтра размера.");if(!child.Name.Equals(".git",StringComparison.OrdinalIgnoreCase)&&(child.Attributes&FileAttributes.ReparsePoint)==0)dirs.Push(child);}
   }
  }
  public static Dictionary<string,FileStamp> Snapshot(string root){return SnapshotCore(root,CancellationToken.None);}
  public static Dictionary<string,FileStamp> SnapshotCore(string root,CancellationToken cancellation){
   var result=new Dictionary<string,FileStamp>(StringComparer.OrdinalIgnoreCase);foreach(var f in Files(root,cancellation))result[f.FullName]=new FileStamp(f);return result;
  }
  public static List<string> Apply(string root,Dictionary<string,FileStamp> before,long limit,string appDirectory){return ApplyCore(root,before,limit,appDirectory,CancellationToken.None);}
  public static List<string> ApplyCore(string root,Dictionary<string,FileStamp> before,long limit,string appDirectory,CancellationToken cancellation){
   if(before==null||limit<1)throw new ArgumentException("Некорректные параметры фильтра размера.");
   var deleted=new List<string>();root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   foreach(var file in Files(root,cancellation)){
    FileStamp old;if(file.Length<=limit||(before.TryGetValue(file.FullName,out old)&&old.Matches(file)))continue;
    if(file.Name.Equals(ExecutionSafety.LockName,StringComparison.OrdinalIgnoreCase)||file.Name.Equals(".gitignore",StringComparison.OrdinalIgnoreCase)||file.Name.Equals(".gitattributes",StringComparison.OrdinalIgnoreCase)||file.Name.Equals(".gitmodules",StringComparison.OrdinalIgnoreCase)||file.Name.EndsWith(".settings.dat",StringComparison.OrdinalIgnoreCase)||file.Name.EndsWith(".ps1",StringComparison.OrdinalIgnoreCase)||file.Name.EndsWith(".bat",StringComparison.OrdinalIgnoreCase)||file.Name.EndsWith(".cmd",StringComparison.OrdinalIgnoreCase)||file.Name.EndsWith(".result.json",StringComparison.OrdinalIgnoreCase))continue;
    if(file.FullName.Equals(root+"Configuration.xml",StringComparison.OrdinalIgnoreCase)||file.FullName.Equals(root+"ConfigDumpInfo.xml",StringComparison.OrdinalIgnoreCase))continue;
    if(file.Name.Equals(".git",StringComparison.OrdinalIgnoreCase)||file.Name.Equals("1c-dump.log",StringComparison.OrdinalIgnoreCase)||file.FullName.Equals(Path.Combine(appDirectory,"settings.dat"),StringComparison.OrdinalIgnoreCase)||file.FullName.Equals(Path.Combine(appDirectory,"OneCConfigExporter.exe"),StringComparison.OrdinalIgnoreCase))continue;
    if(!file.FullName.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Файл находится вне каталога выгрузки.");
    cancellation.ThrowIfCancellationRequested();file.Refresh();if(file.Length<=limit)continue;if((file.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Файл стал ссылкой во время выгрузки: "+file.FullName);file.Delete();deleted.Add(file.FullName);
   }
   return deleted;
  }
 }
}
