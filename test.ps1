param([Parameter(Mandatory=$true)][string]$GitPath)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not (Test-Path -LiteralPath $GitPath -PathType Leaf)) { throw 'Укажите существующий git.exe в -GitPath.' }
& (Join-Path $sourceRoot 'build.ps1')
$testDir = Join-Path $sourceRoot 'tests'
$artifactDir = Join-Path $testDir 'artifacts'
New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceRoot 'dist\OneCConfigExporter.exe') -Destination $artifactDir -Force
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$utility = Join-Path $artifactDir 'OneCConfigExporter.exe'
$fake = Join-Path $artifactDir '1cv8.exe'
$fakeGit = Join-Path $artifactDir 'FakeSlowGit.exe'
$fakeSyncGit = Join-Path $artifactDir 'FakeSyncGit.exe'
& $csc /nologo /target:exe "/out:$fake" (Join-Path $testDir 'FakeDesigner.cs')
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать тестовую 1С.' }
& $csc /nologo /target:exe /reference:System.Core.dll "/out:$fakeGit" (Join-Path $testDir 'FakeSlowGit.cs')
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать тестовый Git.' }
& $csc /nologo /target:exe /reference:System.Core.dll "/out:$fakeSyncGit" (Join-Path $testDir 'FakeSyncGit.cs')
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать тестовый прокси Git.' }
foreach ($testName in @('VerifyReview','VerifyMessages','VerifyRunUi','VerifyGitLog','VerifyGitExecution','VerifyFinalSafety','VerifyCancellation','VerifyStages','VerifyAutomation','VerifyGitRecovery','VerifyLocalFirstSync')) {
 $testExe = Join-Path $artifactDir ($testName + '.exe')
 & $csc /nologo /target:exe "/reference:$utility" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Net.Http.dll /reference:System.Security.dll /reference:System.Web.Extensions.dll /reference:System.Xml.dll "/out:$testExe" (Join-Path $testDir ($testName + '.cs'))
 if ($LASTEXITCODE -ne 0) { throw ('Не удалось собрать ' + $testName) }
 $testGit = if ($testName -eq 'VerifyCancellation') { $fakeGit } else { $GitPath }
 if ($testName -eq 'VerifyGitExecution') { & $testExe $artifactDir $GitPath }
 else { & $testExe $artifactDir $fake $testGit }
 if ($LASTEXITCODE -ne 0) { throw ('Не прошёл ' + $testName) }
}
Write-Host 'Все тесты пройдены. Рабочие базы 1С и GitHub не использовались.'
