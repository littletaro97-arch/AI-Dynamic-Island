[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (Get-Process -Name 'HnMagicClawUI' -ErrorAction SilentlyContinue) {
    throw 'YOYO Claw 正在运行。请先从托盘正常退出后再恢复官方悬浮球。'
}

$statePath = Join-Path $env:APPDATA 'hclaw\floating-ball\state.json'
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
$state.userVisible = $true
$tempPath = "$statePath.tmp"
$state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $tempPath -Encoding utf8
Move-Item -LiteralPath $tempPath -Destination $statePath -Force
Write-Host '官方悬浮球已恢复为显示。重新启动 YOYO Claw 后生效。'
