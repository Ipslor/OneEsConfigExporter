$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path -LiteralPath $csc)) { throw 'Не найден компилятор C# из .NET Framework.' }
& $csc /nologo /warnaserror+ /target:winexe /optimize+ /platform:anycpu /win32manifest:"$root\app.manifest" "/resource:$root\UsageGuide.md,OneCConfigExporter.UsageGuide.md" /reference:System.dll /reference:System.Core.dll /reference:System.Security.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.Xml.dll /out:"$out\OneCConfigExporter.exe" "$root\OneCConfigExporter.cs" "$root\UserSettings.cs" "$root\AppSettingsForm.cs" "$root\ExportSizeFilter.cs" "$root\HelpForm.cs" "$root\BatchBaseForm.cs" "$root\AutomaticScenario.cs" "$root\RuntimeSafety.cs" "$root\GitLog.cs" "$root\ExecutionSafety.cs" "$root\GitRecovery.cs"
if ($LASTEXITCODE -ne 0) { throw "Компиляция завершилась с кодом $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $root 'UsageGuide.md') -Destination $out -Force
Write-Host "Готово: $out\OneCConfigExporter.exe"
