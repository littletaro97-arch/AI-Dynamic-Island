$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'publish-v0.5\YoyoClawCompanion.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "未找到发布程序：$exe。请先运行 Build-Release.ps1。"
}
Start-Process -FilePath $exe
