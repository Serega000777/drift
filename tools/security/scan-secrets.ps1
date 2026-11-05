# Windows wrapper: runs the same scan through Git Bash (installed with Git for Windows).
param([switch]$Staged)
$bash = Join-Path (Split-Path (Get-Command git).Source -Parent) "..\bin\bash.exe"
if (-not (Test-Path $bash)) { $bash = "bash" }
$args = @("tools/security/scan-secrets.sh")
if ($Staged) { $args += "--staged" }
& $bash @args
exit $LASTEXITCODE
