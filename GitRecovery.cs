using System;
using System.IO;
using System.Text;

namespace OneCConfigExporter {
 // Instructions only: no Git process or repository mutation is started by this class.
 internal sealed class GitRecovery {
  public string Text="",CheckCommand="",UnstageCommand="",CommitCommand="";
  static string Literal(string value){return "'"+value.Replace("'","''")+"'";}
  public static GitRecovery Create(string executable,string folder,string error){
   var result=new GitRecovery();
   if(String.IsNullOrWhiteSpace(executable)||String.IsNullOrWhiteSpace(folder)||String.IsNullOrWhiteSpace(error))return result;
   if(executable.IndexOfAny(new[]{'\r','\n','\0'})>=0||folder.IndexOfAny(new[]{'\r','\n','\0'})>=0||!Path.IsPathRooted(folder))return result;
   string git="& "+Literal(executable.Trim())+" -C "+Literal(folder.Trim());
   result.CheckCommand=git+" status --short";
   var text=new StringBuilder("Как продолжить (Windows PowerShell):\r\nНе выполняйте команды изменения репозитория, пока утилита или другой процесс работает с ним.\r\nОткройте PowerShell и сначала проверьте изменения:\r\n");
   text.AppendLine(result.CheckCommand);
   if(error.IndexOf("подготовленные к коммиту",StringComparison.OrdinalIgnoreCase)>=0){
    string scope=" -- . "+Literal(":(exclude)1c-dump.log")+" "+Literal(":(exclude)"+ExecutionSafety.LockName);
    // restore needs HEAD; rm --cached is used only for an unborn repository.
    // Code 1 means HEAD is missing; other failures must not select the rm branch.
    // Refuse to discard an index-only snapshot that differs from the working files.
    result.UnstageCommand=git+" diff --quiet"+scope+"; if ($LASTEXITCODE -eq 0) { "+git+" rev-parse --verify --quiet HEAD > $null; if ($LASTEXITCODE -eq 0) { "+git+" restore --staged"+scope+" } elseif ($LASTEXITCODE -eq 1) { "+git+" rm -r --cached --ignore-unmatch"+scope+" } else { Write-Error 'Не удалось проверить HEAD; индекс не изменён.' } } elseif ($LASTEXITCODE -eq 1) { Write-Error 'Рабочие файлы отличаются от индекса. Сначала сохраните подготовленную версию отдельным коммитом.' } else { Write-Error 'Не удалось проверить рабочие файлы; индекс не изменён.' }";
    result.CommitCommand=git+" commit -m "+Literal("Сохранение подготовленных изменений перед выгрузкой 1С");
    text.Append("\r\nВыберите ОДИН вариант:\r\n1. Снять подготовку к коммиту только в каталоге выгрузки. Файлы на диске сохранятся; утилита сможет включить их в следующий коммит. Если рабочие файлы отличаются от индекса, команда остановится: сначала сохраните подготовленную версию коммитом. Подходит и для репозитория без первого коммита:\r\n").AppendLine(result.UnstageCommand);
    text.Append("\r\n2. Сохранить подготовленные изменения отдельным коммитом. Внимание: в него попадут ВСЕ уже подготовленные изменения репозитория, в том числе вне каталога выгрузки. Сначала проверьте их:\r\n").AppendLine(git+" diff --cached --stat").AppendLine(result.CommitCommand);
    text.Append("\r\nЕсли Git отказывает в снятии с индекса, не добавляйте --force: подготовленная версия может отличаться от файла на диске. Сначала сохраните нужную версию или обратитесь к администратору репозитория.\r\n");
   }else if(error.IndexOf("index.lock",StringComparison.OrdinalIgnoreCase)>=0){
    text.Append("\r\nindex.lock: дождитесь завершения других процессов Git. Если блокировка осталась после сбоя, сначала убедитесь, что никакой Git не работает, и проверьте точный путь блокировки командой:\r\n").AppendLine(git+" rev-parse --git-path index.lock");
    text.Append("Удалять блокировку во время работы Git нельзя. Утилита её автоматически не удаляет.\r\n");
   }else if(error.IndexOf("незавершённой операции",StringComparison.OrdinalIgnoreCase)>=0||error.IndexOf("конфликт",StringComparison.OrdinalIgnoreCase)>=0){
    text.Append("\r\nЗавершите текущую операцию Git и разрешите конфликты по подсказкам git status. Не запускайте новую выгрузку поверх merge/rebase/cherry-pick. Автоматическая отмена операции может потерять результаты ручного разрешения конфликтов.\r\n");
   }else if(error.IndexOf("dubious ownership",StringComparison.OrdinalIgnoreCase)>=0){
    text.Append("\r\nGit не доверяет владельцу каталога. Проверьте владельца и запускайте под нужной учётной записью Windows. Не отключайте защиту для всех каталогов через safe.directory=*.\r\n");
   }else{
    text.Append("\r\nИзучите вывод git status и подробный журнал Git. Не используйте reset --hard, clean -fd или force-push для устранения ошибки без проверки и резервной копии.\r\n");
   }
   text.Append("\r\nПосле устранения причины повторите обработку нужной строки. Если Git не нужен, снимите «Коммит в локальный Git» и «Отправить на GitHub» в параметрах базы: выгрузка возможна без репозитория.");
   result.Text=text.ToString();return result;
  }
 }
}
