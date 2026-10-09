using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Collections.Generic;

namespace OneCConfigExporter {
 public sealed class UserSettings {
  public int Version=1;
  // CommitMessage remains solely for migration of older settings.
  public string Platform="",GitPath="",CommitMessage="",GithubUser="",GithubToken="";
  public bool IgnoreLargeFiles=true;
  public decimal MaxFileMegabytes=30;
  public List<BatchBase> BatchBases=new List<BatchBase>();
  public string[] GithubRepositories=new string[0];
 }
 internal static class SettingsStore {
  internal const int FileLimit=4*1024*1024;
  static readonly byte[] Entropy=Encoding.UTF8.GetBytes("OneCConfigExporter/settings/v1");
  static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=2*1024*1024,RecursionLimit=32};}
  public static UserSettings Load(string path){
   string signature;return LoadTracked(path,out signature);
  }
  public static UserSettings LoadTracked(string path,out string signature){
   signature="unreadable";if(!File.Exists(path)){signature="missing";return null;}
   byte[] encrypted=ExecutionSafety.ReadBounded(path,FileLimit);
   signature=Hash(encrypted);
   byte[] plain=ProtectedData.Unprotect(encrypted,Entropy,DataProtectionScope.CurrentUser);
   try{
    if(plain.Length>FileLimit)throw new InvalidDataException("Расшифрованные настройки слишком велики.");
    var result=Serializer().Deserialize<UserSettings>(new UTF8Encoding(false,true).GetString(plain));
    Validate(result);
    foreach(var profile in result.BatchBases)profile.EnsureCommitMessages(result.CommitMessage);
    return result;
   }finally{Array.Clear(plain,0,plain.Length);}
  }
  public static byte[] Encrypt(UserSettings settings){
   Validate(settings);
   foreach(var profile in settings.BatchBases)profile.EnsureCommitMessages(settings.CommitMessage);
   Validate(settings);
   string json=Serializer().Serialize(settings);
   if(Encoding.UTF8.GetByteCount(json)>FileLimit-4096)throw new InvalidDataException("Настройки слишком велики для сохранения.");
   byte[] plain=Encoding.UTF8.GetBytes(json);
   try{var encrypted=ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser);if(encrypted.Length>FileLimit)throw new InvalidDataException("Зашифрованные настройки слишком велики.");return encrypted;}
   finally{Array.Clear(plain,0,plain.Length);}
  }
  public static void Save(string path,UserSettings settings){byte[] encrypted=Encrypt(settings);using(var writeLock=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))ExecutionSafety.AtomicWrite(path,encrypted);}
  static string Hash(byte[] data){using(var hash=SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(data));}
  public static string Signature(string path){if(!File.Exists(path))return "missing";var info=new FileInfo(path);if(info.Length>FileLimit)return "oversized:"+info.Length+":"+info.LastWriteTimeUtc.Ticks;return Hash(ExecutionSafety.ReadBounded(path,FileLimit));}
  public static string SaveChecked(string path,UserSettings settings,string expected,bool force){byte[] encrypted=Encrypt(settings);using(var writeLock=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   if(!force&&Signature(path)!=expected)throw new InvalidOperationException("Настройки изменены другим экземпляром утилиты. Автоматическое сохранение отменено, чтобы не потерять чужие изменения.");ExecutionSafety.AtomicWrite(path,encrypted);return Hash(encrypted);
  }}
  static void Validate(UserSettings settings){
   if(settings==null||settings.Version!=1)throw new InvalidDataException("Неподдерживаемый формат настроек.");
   if(settings.BatchBases==null||settings.BatchBases.Count>1000||settings.BatchBases.Exists(b=>b==null))throw new InvalidDataException("Некорректный список баз (максимум 1000 записей).");
   if(settings.GithubRepositories!=null&&settings.GithubRepositories.Length>10000)throw new InvalidDataException("Список репозиториев слишком велик.");
   if(settings.BatchBases.Exists(b=>b.Push&&!b.Commit))throw new InvalidDataException("Для отправки на GitHub требуется локальный коммит.");
   if(settings.MaxFileMegabytes<1||settings.MaxFileMegabytes>100000)throw new InvalidDataException("Лимит размера файлов должен быть от 1 до 100000 МБ.");
   long total=0;
   Action<string,int> check=(text,limit)=>{if(text==null)return;if(text.Length>limit||text.IndexOf('\0')>=0)throw new InvalidDataException("Поле настроек слишком длинное либо содержит нулевой символ.");total+=text.Length;if(total>750000)throw new InvalidDataException("Суммарный объём полей настроек слишком велик.");};
   foreach(string value in new[]{settings.Platform,settings.GitPath,settings.CommitMessage,settings.GithubUser,settings.GithubToken})check(value,4096);
   foreach(var b in settings.BatchBases){foreach(string credential in new[]{b.DbUser,b.DbPassword,b.RepoUser,b.RepoPassword})if(credential!=null&&credential.IndexOfAny(new[]{'\r','\n'})>=0)throw new InvalidDataException("Реквизиты не должны содержать переносы строк.");}
   foreach(string credential in new[]{settings.GithubUser,settings.GithubToken})if(credential!=null&&credential.IndexOfAny(new[]{'\r','\n'})>=0)throw new InvalidDataException("Реквизиты GitHub не должны содержать переносы строк.");
   foreach(var b in settings.BatchBases)foreach(string value in new[]{b.Name,b.Connection,b.FilePath,b.Server,b.Database,b.DbUser,b.DbPassword,b.RepoUser,b.RepoPassword,b.Output,b.GithubRepo,b.FullCommitMessage,b.IncrementalCommitMessage})check(value,4096);
   foreach(string name in settings.GithubRepositories??new string[0])check(name,256);
  }
 }
}
