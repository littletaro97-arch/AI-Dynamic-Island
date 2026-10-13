param([string]$IsccPath, [switch]$PortableOnly)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'YoyoClawCompanion\YoyoClawCompanion.csproj'
[xml]$metadata = Get-Content -LiteralPath $project
$version = $metadata.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
$bridgeSdk = Join-Path $PSScriptRoot 'magicore-bridge\node_modules\@magicore\sdk\package.json'
if (-not (Test-Path -LiteralPath $bridgeSdk)) { throw '缺少 Magicore vendor SDK；请从 YOYO 安装包恢复，不要运行 npm ci。' }
$publishDirectory = Join-Path $PSScriptRoot "publish-v$version"
if (Test-Path -LiteralPath $publishDirectory) { throw '发布目录已存在，请先将旧候选输出另存，再从空目录重新打包。' }
$sourceRevision = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw '无法确认发布源码提交。' }
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "发布失败：$LASTEXITCODE" }
$publishedExecutable = Join-Path $publishDirectory 'YoyoClawCompanion.exe'
$productVersion = (Get-Item -LiteralPath $publishedExecutable).VersionInfo.ProductVersion.Split('+')[0].Trim()
if ($productVersion -ne $version) { throw "程序产品版本 $productVersion 与发布版本 $version 不一致，停止打包。" }
$informationalVersion = (Get-Item -LiteralPath $publishedExecutable).VersionInfo.ProductVersion
if (-not $informationalVersion.EndsWith("+$sourceRevision")) { throw '主程序构建提交与当前源码不一致，停止打包。' }
$moduleSource = Join-Path $PSScriptRoot 'MagicStatusbarAdapter\bin\Release\net8.0-windows10.0.19041.0\MagicStatusbarAdapter.dll'
# The SDK removes RuntimeIdentifier from this library project reference.
$moduleRevision = (Get-Item -LiteralPath $moduleSource).VersionInfo.ProductVersion
if (-not $moduleRevision.EndsWith("+$sourceRevision")) { throw 'Magic 模块构建提交与当前源码不一致，停止打包。' }
foreach ($pair in @(
    @($moduleSource, (Join-Path $publishDirectory 'MagicStatusbarAdapter.dll')),
    @((Join-Path $PSScriptRoot 'MagicStatusbarAdapter\native\MagicBarLayout.dll'), (Join-Path $publishDirectory 'MagicBarLayout.dll'))
)) {
    if ((Get-FileHash -LiteralPath $pair[0] -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $pair[1] -Algorithm SHA256).Hash) { throw 'Magic 模块与最新构建不一致，停止打包。' }
}
Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' -File | Remove-Item -Force
$assetPath = Join-Path $artifacts "AI-Dynamic-Island-v$version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $assetPath -CompressionLevel Optimal -Force
if (-not $PortableOnly) {
    if (-not $IsccPath) {
        $candidates = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'), (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
        $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (-not $IsccPath) { throw '便携包已生成；安装包需要 Inno Setup 6。请使用 -IsccPath 指定 ISCC.exe。' }
    & $IsccPath "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer\AI-Dynamic-Island.iss')
    if ($LASTEXITCODE -ne 0) { throw "安装包编译失败：$LASTEXITCODE" }
}
Get-ChildItem -LiteralPath $artifacts -File | Where-Object { $_.Name -like "AI-Dynamic-Island-v$version-*" } | Get-FileHash -Algorithm SHA256
