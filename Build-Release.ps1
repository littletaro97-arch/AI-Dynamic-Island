$ErrorActionPreference = 'Stop'
$bridgeSdk = Join-Path $PSScriptRoot 'magicore-bridge\node_modules\@magicore\sdk\package.json'
if (-not (Test-Path -LiteralPath $bridgeSdk)) {
    throw '缺少 Magicore vendor SDK。不要运行 npm ci；请从当前 YOYO 安装包恢复 magicore-bridge\node_modules\@magicore。'
}
dotnet publish (Join-Path $PSScriptRoot 'YoyoClawCompanion\YoyoClawCompanion.csproj') `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true `
    -o (Join-Path $PSScriptRoot 'publish-v0.6.0')
if ($LASTEXITCODE -ne 0) { throw "发布失败，退出代码：$LASTEXITCODE" }
$publishDirectory = Join-Path $PSScriptRoot 'publish-v0.6.0'
$assetPath = Join-Path $PSScriptRoot 'AI-Dynamic-Island-win-x64.zip'
if (Test-Path -LiteralPath $assetPath) { Remove-Item -LiteralPath $assetPath -Force }
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $assetPath -CompressionLevel Optimal
Write-Host "发布完成：$(Join-Path $publishDirectory 'YoyoClawCompanion.exe')"
Write-Host "GitHub Release 资产：$assetPath"
