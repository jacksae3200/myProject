param(
    [Parameter(Mandatory = $true)][string]$ServerPath,
    [Parameter(Mandatory = $true)][string]$ModelPath,
    [int]$Port = 8080,
    [int]$Threads = 4
)
$ErrorActionPreference = 'Stop'
$serverFile = (Resolve-Path -LiteralPath $ServerPath).Path
$modelFile = (Resolve-Path -LiteralPath $ModelPath).Path
$logDirectory = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$processArguments = @('-m', ('"' + $modelFile + '"'), '-l', 'ko', '--host', '127.0.0.1', '--port', $Port, '-t', $Threads, '-ng')
$serverProcess = Start-Process -FilePath $serverFile -ArgumentList $processArguments -WorkingDirectory (Split-Path -Parent $serverFile) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory 'stt.stdout.log') -RedirectStandardError (Join-Path $logDirectory 'stt.stderr.log')
Write-Output "Local Korean STT started. PID=$($serverProcess.Id), URL=http://127.0.0.1:$Port/inference"
Write-Output "Logs: $logDirectory"
Write-Output "Stop: Stop-Process -Id $($serverProcess.Id)"
