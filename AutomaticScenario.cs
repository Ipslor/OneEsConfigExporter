using System;
using System.IO;
using System.Text;

namespace OneCConfigExporter {
 internal static class AutomaticScenario {
  static string Literal(string text){return "'"+text.Replace("'","''")+"'";}
  public static string Write(string scriptPath,string exePath,UserSettings settings){
   scriptPath=Path.GetFullPath(scriptPath);exePath=Path.GetFullPath(exePath);if(!scriptPath.EndsWith(".ps1",StringComparison.OrdinalIgnoreCase)||scriptPath.Equals(exePath,StringComparison.OrdinalIgnoreCase))throw new IOException("Сценарий должен сохраняться в отдельный файл .ps1.");string folder=Path.GetDirectoryName(scriptPath);
   string prefix=Path.GetFileNameWithoutExtension(scriptPath),configName=prefix+".settings.dat";
   string config=Path.Combine(folder,configName);byte[] encrypted=SettingsStore.Encrypt(settings);
   string script=@"# Автоматическая выгрузка 1С. Запускайте под тем же пользователем Windows,
# который создал этот сценарий и зашифрованный файл настроек.
# Для планировщика: powershell.exe -NoProfile -File ""ПУТЬ-К-ЭТОМУ-ФАЙЛУ.ps1""
param(
 [string]$UtilityExe = "+Literal(Path.GetFullPath(exePath))+@",
 [ValidateRange(1,10080)][int]$TimeoutMinutes = 180
)
$ErrorActionPreference = 'Stop'
$config = Join-Path $PSScriptRoot "+Literal(configName)+@"
$result = Join-Path $PSScriptRoot "+Literal(prefix+".result.json")+@"
$lock = $null
function Write-ResultState([string]$state, [int]$code, [string]$message) {
 $report = @{ State=$state; Completed=[DateTimeOffset]::UtcNow.ToString('o'); ExitCode=$code; Error=$message } | ConvertTo-Json
 $temporary = $result + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
 try {
  [IO.File]::WriteAllText($temporary, $report, (New-Object Text.UTF8Encoding($true)))
  if ([IO.File]::Exists($result)) { [IO.File]::Replace($temporary, $result, $null) }
  else { [IO.File]::Move($temporary, $result) }
 } finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}
try {
 if (-not (Test-Path -LiteralPath $UtilityExe -PathType Leaf)) { throw 'Не найден exe утилиты' }
 if (-not (Test-Path -LiteralPath $config -PathType Leaf)) { throw 'Не найден файл настроек сценария' }
 $lock = [IO.File]::Open($config + '.lock', [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
 Write-ResultState 'Starting' 2 ''
 $arguments = '--run-batch --settings ""' + $config + '"" --result ""' + $result + '""'
 $process = Start-Process -FilePath $UtilityExe -ArgumentList $arguments -PassThru -WindowStyle Hidden
 if (-not $process.WaitForExit($TimeoutMinutes * 60000)) {
  & (Join-Path $env:SystemRoot 'System32\taskkill.exe') /PID $process.Id /T /F | Out-Null
  $message = 'Истекло время выполнения.'
  if ($LASTEXITCODE -ne 0) { $message += ' Не удалось подтвердить остановку дерева процессов; проверьте процессы вручную.' }
  Write-ResultState 'TimedOut' 124 $message
  Write-Error $message -ErrorAction Continue
  exit 124
 }
 $process.Refresh()
 $code = $process.ExitCode
 try { $savedReport = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json } catch { $savedReport = $null }
 if ($null -eq $savedReport -or $savedReport.State -eq 'Starting' -or $savedReport.State -eq 'Running') {
  $code = if ($code -eq 0) { 2 } else { $code }
  Write-ResultState 'Failed' $code 'Утилита не записала итоговый отчёт.'
 }
 if ($code -ne 0) { Write-Warning ('Очередь завершилась с ошибкой. Код: ' + $code + '. Результат: ' + $result) }
 exit $code
} catch {
 $message = $_.Exception.Message
 if ($null -ne $lock) { try { Write-ResultState 'Failed' 2 $message } catch {} }
 Write-Error $message -ErrorAction Continue
 exit 2
} finally {
 if ($null -ne $lock) { $lock.Dispose() }
}
";
   // The same lock is used by generated scripts: never overwrite a snapshot being executed.
   Directory.CreateDirectory(folder);
   using(var pairLock=new FileStream(config+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
    byte[] previous=File.Exists(config)?ExecutionSafety.ReadBounded(config,SettingsStore.FileLimit):null;
    try{
     ExecutionSafety.AtomicWrite(config,encrypted);
     ExecutionSafety.AtomicWrite(scriptPath,ExecutionSafety.Utf8WithBom(script));return config;
    }catch(Exception original){
     try{if(previous!=null)ExecutionSafety.AtomicWrite(config,previous);else File.Delete(config);}
     catch(Exception rollback){throw new IOException("Сценарий не сохранён и восстановить прежний снимок не удалось. Проверьте файлы сценария. "+rollback.Message,original);}
     throw;
    }
   }
  }
 }
}
