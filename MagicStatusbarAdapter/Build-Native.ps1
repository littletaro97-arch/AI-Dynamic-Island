$ErrorActionPreference='Stop'
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsRoot=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsRoot) {throw '缺少 MSVC x64 构建工具'}
$nativeDir=Join-Path $PSScriptRoot 'native'
$compilerSetup=Join-Path $vsRoot 'VC\Auxiliary\Build\vcvars64.bat'
$buildCommand=Join-Path $nativeDir 'build.cmd'
@"
@echo off
chcp 65001 >nul
call "$compilerSetup" >nul
if errorlevel 1 exit /b 1
cd /d "$nativeDir"
cl /utf-8 /nologo /O2 /MT /EHsc /std:c++17 /LD MagicBarLayout.cpp /link /OUT:MagicBarLayout.dll ole32.lib oleaut32.lib user32.lib
"@ | ForEach-Object { [System.IO.File]::WriteAllText($buildCommand,$_ ,[System.Text.UTF8Encoding]::new($false)) }
& cmd.exe /d /c $buildCommand
if ($LASTEXITCODE -ne 0) {throw '原生布局探测器编译失败'}
