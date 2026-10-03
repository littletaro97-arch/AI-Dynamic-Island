#ifndef AppVersion
  #define AppVersion "0.7.3"
#endif
[Setup]
AppId={{907C6427-5D86-489B-B8F9-C65351AE2611}
AppName=AI Dynamic Island
AppVersion={#AppVersion}
AppPublisher=LittleTaro
AppPublisherURL=https://github.com/littletaro97-arch/AI-Dynamic-Island
DefaultDirName={code:GetDefaultInstallDir}
UsePreviousAppDir=yes
; ShouldSkipPage skips the directory page only when the requested path matches.
DisableDirPage=no
AppendDefaultDirName=no
AlwaysShowDirOnReadyPage=yes
SetupLogging=yes
SetupIconFile=..\YoyoClawCompanion\Assets\App.ico
DefaultGroupName=AI Dynamic Island
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=AI-Dynamic-Island-v{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
InfoBeforeFile=install-guide.txt
UninstallDisplayIcon={app}\YoyoClawCompanion.exe
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl"
[LangOptions]
LanguageName=简体中文
LanguageID=$0804
LanguageCodePage=0
[Messages]
SetupWindowTitle=安装向导 - %1
UninstallAppFullTitle=卸载 %1
ButtonBack=< 上一步
ButtonNext=下一步 >
ButtonInstall=安装
ButtonCancel=取消
ButtonFinish=完成
ButtonOK=确定
ButtonYes=是
ButtonNo=否
ButtonBrowse=浏览...
ButtonWizardBrowse=浏览...
WelcomeLabel1=欢迎使用 [name] 安装向导
WelcomeLabel2=此向导将安装 [name/ver]。%n%n升级前请从托盘退出旧版本。
ClickNext=点击“下一步”继续，或点击“取消”退出。
WizardInfoBefore=安装与卸载说明
InfoBeforeLabel=继续之前，请阅读以下使用说明。
InfoBeforeClickLabel=准备好后，点击“下一步”。
WizardSelectDir=选择安装位置
SelectDirDesc=将 [name] 安装在哪里？
SelectDirLabel3=程序将安装到以下目录。
SelectDirBrowseLabel=点击“下一步”继续，或点击“浏览”选择其他目录。
WizardSelectProgramGroup=选择开始菜单目录
WizardSelectTasks=选择附加选项
WizardReady=准备安装
WizardInstalling=正在安装
FinishedHeadingLabel=[name] 安装完成
FinishedLabelNoIcons=程序已安装完成。点击“完成”退出安装向导。
FinishedLabel=程序已安装完成，可以通过快捷方式启动。点击“完成”退出安装向导。
ConfirmUninstall=确定卸载 %1 及其程序文件吗？用户设置和日志将保留。
UninstallStatusLabel=正在卸载 %1，请稍候。
UninstalledAll=%1 已卸载完成。用户设置和日志已保留。
[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked
[Files]
Source: "..\publish-v{#AppVersion}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\AI Dynamic Island"; Filename: "{app}\YoyoClawCompanion.exe"
Name: "{group}\卸载 AI Dynamic Island"; Filename: "{uninstallexe}"
Name: "{autodesktop}\AI Dynamic Island"; Filename: "{app}\YoyoClawCompanion.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\YoyoClawCompanion.exe"; Description: "启动 AI Dynamic Island"; Flags: nowait postinstall skipifsilent
[Code]
function PreviousInstallDir: String;
var AppPath: String;
begin
  Result := '';
  { Read both registry views for upgrades from older installer configurations. }
  if not RegQueryStringValue(HKCU64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{907C6427-5D86-489B-B8F9-C65351AE2611}_is1', 'Inno Setup: App Path', AppPath) then
    RegQueryStringValue(HKCU32, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{907C6427-5D86-489B-B8F9-C65351AE2611}_is1', 'Inno Setup: App Path', AppPath);
  if (AppPath <> '') and FileExists(AddBackslash(AppPath) + 'unins000.exe') then
    Result := RemoveBackslashUnlessRoot(AppPath);
end;

function GetDefaultInstallDir(Param: String): String;
begin
  Result := PreviousInstallDir;
  if Result = '' then
    Result := ExpandConstant('{localappdata}\Programs\AI Dynamic Island');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
var Previous: String;
begin
  Previous := PreviousInstallDir;
  { Skip destination confirmation only for an existing registered installation
    at the exact same path. A changed /DIR still follows the normal wizard. }
  Result := (PageID = wpSelectDir) and (Previous <> '') and
    (CompareText(RemoveBackslashUnlessRoot(WizardDirValue), Previous) = 0);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AIDynamicIsland', StartupCommand) then
      if Pos('"' + ExpandConstant('{app}\YoyoClawCompanion.exe') + '"', StartupCommand) = 1 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AIDynamicIsland');
  end;
end;
