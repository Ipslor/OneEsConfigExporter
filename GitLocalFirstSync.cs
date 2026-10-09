using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OneCConfigExporter {
 internal static class GitLocalFirstSync {
  static string Quote(string value){var text=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"')text.Append('\\',slashes*2+1);else text.Append('\\',slashes);text.Append(c);slashes=0;}text.Append('\\',slashes*2).Append('"');return text.ToString();}
  static bool ObjectId(string value){return Regex.IsMatch(value??"",@"\A(?:[0-9a-f]{40}|[0-9a-f]{64})\z");}
  // Only fixed metadata queries may return unmasked data to the internal state machine.
  // Display and disk logs remain masked by the process runner.
  public static bool IsMachineQuery(string command){return command=="symbolic-ref --quiet HEAD"||command=="symbolic-ref --quiet --short HEAD"||command=="rev-parse --verify HEAD^{commit}"||command=="rev-parse --absolute-git-dir"||Regex.IsMatch(command,@"\Arev-parse --verify (?:[0-9a-f]{40}|[0-9a-f]{64})\^\{tree\}\z")||Regex.IsMatch(command,@"\Arev-parse --verify refs/onecexporter-fetch/[0-9a-f]{32}\^\{commit\}\z");}
  public static bool IsPorcelainPush(string command){return command.StartsWith("push --porcelain --no-force --no-mirror --no-follow-tags ",StringComparison.Ordinal);}
  public static bool IsIndexQuery(string command){return command=="ls-files -v -- :/"||command.StartsWith("ls-files -v -- . ",StringComparison.Ordinal);}
  public static bool HiddenIndexRows(string output){foreach(string line in (output??"").Split('\n'))if(line.Length>=2&&line[1]==' '&&(line[0]=='S'||Char.IsLower(line[0])))return true;return false;}
  public static bool HistoryRejected(string output,string branch,string source){
   int records=0;bool rejected=false;
   foreach(string line in (output??"").Split('\n')){string[] fields=line.TrimEnd('\r').Split('\t');if(fields.Length!=3||fields[1]!=source+":refs/heads/"+branch)continue;records++;rejected=fields[0]=="!"&&(fields[2]=="[rejected] (non-fast-forward)"||fields[2]=="[rejected] (fetch first)");}
   return records==1&&rejected;
  }
  static string Failure(string phase,Tuple<int,string> result){return "Коммит сохранён локально. Автоматическое согласование Git/GitHub остановлено: "+phase+".\r\n"+result.Item2;}
  static async Task<string> HistoryProblem(Func<string,bool,Task<Tuple<int,string>>> run,string directory){
   if(await Task.Run(()=>File.Exists(Path.Combine(directory,"info","grafts"))))return "В Git есть info/grafts: история переопределена. Отправка остановлена; сначала согласуйте такую историю вручную.";
   var replacements=await run("for-each-ref --format=%(refname) refs/replace/",false);if(replacements.Item1!=0)return Failure("не удалось проверить подмену объектов Git",replacements);if(!String.IsNullOrWhiteSpace(replacements.Item2))return "В Git есть refs/replace: объекты истории переопределены. Отправка остановлена; сначала согласуйте такую историю вручную.";
   return null;
  }
  static async Task<string> IndexProblem(Func<string,bool,Task<Tuple<int,string>>> run){var flags=await run("ls-files -v -- :/",false);if(flags.Item1!=0)return Failure("не удалось проверить скрытые флаги индекса",flags);return HiddenIndexRows(flags.Item2)?"В индексе Git есть assume-unchanged/skip-worktree. Git может пропустить изменения файлов. Отправка остановлена; снимите эти флаги вручную и повторите выгрузку.":null;}
  static async Task<Tuple<int,string>> DirectoryQuery(Func<string,bool,Task<Tuple<int,string>>> run){var directory=await run("rev-parse --absolute-git-dir",false);if(directory.Item1==0&&(!Path.IsPathRooted(directory.Item2)||!await Task.Run(()=>Directory.Exists(directory.Item2))))return Tuple.Create(-1,"Служебный каталог Git недоступен.");return directory;}
  static async Task<string> Ready(Func<string,bool,Task<Tuple<int,string>>> run,string branch,string commit,Func<bool> cancelled){
   if(cancelled())return "Пакет остановлен пользователем.";
   var current=await run("symbolic-ref --quiet HEAD",false);if(current.Item1!=0)return Failure("не удалось проверить текущую ветку",current);if(current.Item2!="refs/heads/"+branch)return "Текущая ветка Git изменилась. Согласование остановлено без переключения веток.";
   var head=await run("rev-parse --verify HEAD^{commit}",false);if(head.Item1!=0)return Failure("не удалось проверить локальный коммит",head);if(head.Item2!=commit)return "Локальный HEAD изменился другим процессом. Согласование остановлено.";
   var directory=await DirectoryQuery(run);if(directory.Item1!=0)return Failure("не удалось проверить служебный каталог Git",directory);
   string marker=await Task.Run(()=>{foreach(string name in new[]{"MERGE_HEAD","CHERRY_PICK_HEAD","REVERT_HEAD","BISECT_START","rebase-merge","rebase-apply","sequencer","index.lock"}){string path=Path.Combine(directory.Item2,name);if(File.Exists(path)||Directory.Exists(path))return name;}return null;});if(marker!=null)return "Git занят или находится в незавершённой операции ("+marker+"). Автоматическое согласование не выполнялось.";
   string problem=await HistoryProblem(run,directory.Item2);if(problem!=null)return problem;problem=await IndexProblem(run);if(problem!=null)return problem;
   var staged=await run("diff --cached --no-ext-diff --quiet --ignore-submodules=none",false);if(staged.Item1==1)return "В репозитории остались подготовленные изменения, в том числе возможно вне каталога выгрузки. Сохраните их отдельно: автоматическое согласование не меняет чужой индекс.";if(staged.Item1!=0)return Failure("не удалось проверить индекс",staged);
   var working=await run("diff --no-ext-diff --quiet --ignore-submodules=none",false);if(working.Item1==1)return "Рабочие отслеживаемые файлы отличаются от локального коммита. Сначала сохраните изменения; автоматическое согласование остановлено.";if(working.Item1!=0)return Failure("не удалось проверить рабочие файлы",working);
   return cancelled()?"Пакет остановлен пользователем.":null;
  }
  public static async Task<string> PushAsync(Func<string,bool,Task<Tuple<int,string>>> run,string url,string branch,Func<bool> cancelled,Action<string> stage,Action<string> record,Action<string> completed,Func<string,string> describeFailure){
   if(!Regex.IsMatch(url??"",@"\Ahttps://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\.git\z")||String.IsNullOrWhiteSpace(branch))return "Некорректный адрес GitHub или текущая ветка. Отправка остановлена.";
   if(cancelled())return "Пакет остановлен пользователем.";
   stage("Проверка локального Git-снимка перед отправкой…");
   var valid=await run("check-ref-format "+Quote("refs/heads/"+branch),false);if(valid.Item1!=0)return Failure("некорректная текущая ветка",valid);
   var initialRef=await run("symbolic-ref --quiet HEAD",false);if(initialRef.Item1!=0||initialRef.Item2!="refs/heads/"+branch)return "Не удалось подтвердить текущую ветку Git. Отправка остановлена.";
   var head=await run("rev-parse --verify HEAD^{commit}",false);if(head.Item1!=0||!ObjectId(head.Item2))return Failure("не удалось определить локальный коммит",head);string local=head.Item2;
   var localTree=await run("rev-parse --verify "+local+"^{tree}",false);if(localTree.Item1!=0||!ObjectId(localTree.Item2))return Failure("не удалось определить локальный снимок файлов",localTree);
   var initialDirectory=await DirectoryQuery(run);if(initialDirectory.Item1!=0)return Failure("не удалось проверить служебный каталог Git",initialDirectory);
   string initialProblem=await HistoryProblem(run,initialDirectory.Item2);if(initialProblem!=null)return initialProblem;initialProblem=await IndexProblem(run);if(initialProblem!=null)return initialProblem;
   if(cancelled())return "Пакет остановлен пользователем.";
   string push="push --porcelain --no-force --no-mirror --no-follow-tags "+Quote(url)+" "+Quote(local+":refs/heads/"+branch);
   stage("Отправка на GitHub…");var first=await run(push,true);if(first.Item1==0){completed("Изменения отправлены на GitHub");return null;}
   if(cancelled())return "Пакет остановлен пользователем. Локальный коммит сохранён.";
   if(!HistoryRejected(first.Item2,branch,local))return describeFailure(first.Item2);
   record("GitHub отклонил обычную отправку из-за расхождения историй. Локальная выгрузка принята за источник истины.");
   stage("Проверка Git перед автоматическим согласованием…");string error=await Ready(run,branch,local,cancelled);if(error!=null)return error;
   stage("Получение удалённой истории GitHub…");
   string fetchRef="refs/onecexporter-fetch/"+Guid.NewGuid().ToString("N");
   // A private, unique ref prevents a concurrent fetch from replacing FETCH_HEAD
   // between the fetch and merge. Never use a force refspec or update origin refs.
   var fetch=await run("fetch --atomic --no-auto-maintenance --no-tags --no-write-fetch-head --no-recurse-submodules "+Quote(url)+" "+Quote("refs/heads/"+branch+":"+fetchRef),true);
   if(cancelled())return Failure("получение истории прервано; временная ссылка могла остаться",fetch);
   // Even a failing fetch may have updated its ref before a later error.
   var remote=await run("rev-parse --verify "+fetchRef+"^{commit}",false);
   if(fetch.Item1!=0){if(remote.Item1==0&&ObjectId(remote.Item2))await CleanupAsync(run,fetchRef,remote.Item2,record);return Failure("не удалось получить удалённую ветку",fetch);}
   if(remote.Item1!=0||!ObjectId(remote.Item2)){record("Не удалось проверить временную ссылку "+fetchRef+"; она могла остаться для ручной проверки.");return Failure("не удалось определить полученный удалённый коммит",remote);}
   string outcome;
   try{outcome=await ReconcileAsync(run,url,branch,local,localTree.Item2,remote.Item2,cancelled,stage,record,completed,describeFailure);}
   catch(Exception ex){outcome="Автоматическое согласование Git/GitHub остановлено: "+ex.Message+". Локальные коммиты и созданные резервные ветки сохранены; автоматический откат не выполнялся.";}
   if(!cancelled())await CleanupAsync(run,fetchRef,remote.Item2,record);
   return outcome;
  }
  static async Task CleanupAsync(Func<string,bool,Task<Tuple<int,string>>> run,string reference,string expected,Action<string> record){try{var cleanup=await run("update-ref -d "+Quote(reference)+" "+expected,false);if(cleanup.Item1!=0)record("Не удалось удалить временную ссылку "+reference+". Рабочие файлы не затронуты; ответ Git: "+cleanup.Item2);}catch(Exception ex){record("Временная ссылка "+reference+" могла остаться после ошибки очистки: "+ex.Message);}}
  static async Task<string> ReconcileAsync(Func<string,bool,Task<Tuple<int,string>>> run,string url,string branch,string local,string localTree,string remote,Func<bool> cancelled,Action<string> stage,Action<string> record,Action<string> completed,Func<string,string> describeFailure){
   string error=await Ready(run,branch,local,cancelled);if(error!=null)return error;
   var ancestor=await run("merge-base --is-ancestor "+remote+" "+local,false);if(ancestor.Item1!=0&&ancestor.Item1!=1)return Failure("не удалось сравнить истории",ancestor);
   string finalCommit=local;
   if(ancestor.Item1==1){
    stage("Сохранение резервных веток Git…");string prefix="onecexporter-backup/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss'Z'")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+"/";
    var backupLocal=await run("branch "+Quote(prefix+"local")+" "+local,false);if(backupLocal.Item1!=0)return Failure("не удалось сохранить локальную резервную ветку",backupLocal);
    var backupRemote=await run("branch "+Quote(prefix+"github")+" "+remote,false);if(backupRemote.Item1!=0)return Failure("не удалось сохранить удалённую резервную ветку",backupRemote);
    record("Резервные ветки Git: "+prefix+"local и "+prefix+"github.");completed("Сохранены резервные ветки локальной и удалённой истории Git");
    error=await Ready(run,branch,local,cancelled);if(error!=null)return error;
    stage("Согласование историй Git: локальные файлы имеют приоритет…");
    var merge=await run("merge --no-ff --no-squash --commit --no-autostash --allow-unrelated-histories --strategy=ours --no-edit -m "+Quote("Согласование GitHub: локальная выгрузка 1С принята за актуальную")+" "+remote,false);if(merge.Item1!=0||cancelled())return Failure("не удалось согласовать истории; резервные ветки сохранены",merge);
    var head=await run("rev-parse --verify HEAD^{commit}",false);if(head.Item1!=0||!ObjectId(head.Item2))return Failure("не удалось проверить коммит согласования",head);finalCommit=head.Item2;
   }
   var finalTree=await run("rev-parse --verify "+finalCommit+"^{tree}",false);if(finalTree.Item1!=0||finalTree.Item2!=localTree)return "Проверка согласования не пройдена: снимок локальных файлов изменился. Отправка остановлена; проверьте резервные ветки и журнал Git. Автоматический откат не выполнялся.";
   var localParent=await run("merge-base --is-ancestor "+local+" "+finalCommit,false);var remoteParent=await run("merge-base --is-ancestor "+remote+" "+finalCommit,false);if(localParent.Item1!=0||remoteParent.Item1!=0)return "Проверка согласования не пройдена: обе исходные истории должны сохраняться в итоговом коммите. Отправка остановлена.";
   error=await Ready(run,branch,finalCommit,cancelled);if(error!=null)return error;
   if(ancestor.Item1==1)completed("Истории Git согласованы: локальные файлы неизменны, удалённая история сохранена");
   stage("Повторная отправка на GitHub после согласования…");
   // Push the verified object ID, not a potentially changing HEAD. Exactly one retry.
   var retry=await run("push --porcelain --no-force --no-mirror --no-follow-tags "+Quote(url)+" "+Quote(finalCommit+":refs/heads/"+branch),true);
   if(retry.Item1!=0)return "Локальный коммит сохранён. Повторная отправка после согласования не выполнена. Новых автоматических попыток в этом запуске не будет.\r\n"+describeFailure(retry.Item2);
   completed("Изменения отправлены на GitHub");return null;
  }
 }
}
