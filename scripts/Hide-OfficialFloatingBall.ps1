[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$running = Get-Process -Name 'HnMagicClawUI' -ErrorAction SilentlyContinue
if ($running) {
    throw 'YOYO Claw 正在运行。请先从托盘正常退出 YOYO Claw，再重新运行本脚本；脚本不会强制结束官方进程。'
}

$statePath = Join-Path $env:APPDATA 'hclaw\floating-ball\state.json'
if (-not (Test-Path -LiteralPath $statePath)) {
    throw "未找到官方悬浮球状态文件：$statePath"
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupPath = "$statePath.backup_$stamp"
Copy-Item -LiteralPath $statePath -Destination $backupPath

$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ($state.schemaVersion -ne 1) {
    throw "不支持的状态文件版本：$($state.schemaVersion)。备份位于：$backupPath"
}

$state.userVisible = $false
$tempPath = "$statePath.tmp"
$state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $tempPath -Encoding utf8
Move-Item -LiteralPath $tempPath -Destination $statePath -Force

$verified = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ($verified.userVisible -ne $false) {
    throw "写入后校验失败。可使用备份恢复：$backupPath"
}

Write-Host '官方悬浮球已设置为隐藏。重新启动 YOYO Claw 后生效。'
Write-Host "备份：$backupPath"
