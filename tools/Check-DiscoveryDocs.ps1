$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'YoyoClawCompanion/YoyoClawCompanion.csproj')
$version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
$manifest = Get-Content -Raw -LiteralPath (Join-Path $root 'docs/update.json') | ConvertFrom-Json
$readme = Get-Content -Raw -LiteralPath (Join-Path $root 'README.md')
if ($manifest.tag_name -ne "v$version") { throw '项目版本与公开发布清单不一致（此检查不修改清单）' }
foreach ($suffix in @('portable.zip','setup.exe')) {
    $expected = "AI-Dynamic-Island-v$version-win-x64-$suffix"
    $asset = @($manifest.assets | Where-Object { $_.name -eq $expected })
    if ($asset.Count -ne 1 -or $asset[0].browser_download_url -notmatch ('/v' + [regex]::Escape($version) + '/' + [regex]::Escape($expected) + '$')) { throw "清单资产名称或 URL 不一致：$expected" }
}
if ($readme -notmatch '^# AI Dynamic Island\r?\n') { throw 'README 首页应使用无版本项目名' }
if ($readme -match 'AI-Dynamic-Island-v\d+\.\d+\.\d+-win-x64|publish-v\d+\.\d+\.\d+') { throw 'README 含易过期的完整包名或发布目录' }
if ($readme -notmatch 'https://github.com/littletaro97-arch/AI-Dynamic-Island/releases/latest') { throw '缺少稳定下载入口' }
Write-Output "PASS: project=$version, manifest=$($manifest.tag_name), both asset names/URLs, versionless README and latest download. Historical release notes are allowed."
