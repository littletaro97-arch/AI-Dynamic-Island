param([string]$IsccPath, [switch]$PortableOnly)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'YoyoClawCompanion\YoyoClawCompanion.csproj'
[xml]$metadata = Get-Content -LiteralPath $project
$version = [string]$metadata.Project.PropertyGroup.Version
$bridgeSdk = Join-Path $PSScriptRoot 'magicore-bridge\node_modules\@magicore\sdk\package.json'
if (-not (Test-Path -LiteralPath $bridgeSdk)) { throw '缺少 Magicore vendor SDK；请从 YOYO 安装包恢复，不要运行 npm ci。' }
$publishDirectory = Join-Path $PSScriptRoot "publish-v$version"
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "发布失败：$LASTEXITCODE" }
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
