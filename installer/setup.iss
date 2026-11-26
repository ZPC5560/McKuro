; McKuro · 鸣潮启动器 — Inno Setup 安装脚本
; 由 GitHub Actions (setup job) 在 Windows runner 上编译为 Setup.exe
; 本地调试: 安装 Inno Setup 6 后运行 ISCC.exe setup.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.3.8.5"
#endif
#ifndef MyAppPublishDir
  #define MyAppPublishDir "..\src\McKuro\bin\Release\net10.0\win-x64\publish"
#endif

#define MyAppName "McKuro · 鸣潮启动器"
#define MyAppExeName "McKuro.exe"

[Setup]
AppId={{8E4B7C2A-5D3F-4A9B-B6C1-2E7F0D9A4C55}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=McKuro
DefaultDirName={code:DefaultDir}
DefaultGroupName=McKuro
UninstallDisplayIcon={app}\{#MyAppExeName}
; 静默更新支持:安装器自动关闭运行中的 McKuro,装完自动重启(应用内自更新传 /VERYSILENT 即零向导)
CloseApplications=yes
RestartApplications=yes
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputDir=..\artifacts\setup
OutputBaseFilename=McKuro-setup-{#MyAppVersion}
SetupIconFile=..\src\McKuro\Assets\app.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; 允许命令行切到当前用户安装(/CURRENTUSER,无需 UAC):
; 无管理员权限的用户也能静默更新;向导模式不弹权限选择框,保持默认全用户安装
PrivilegesRequiredOverridesAllowed=commandline

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 排除调试符号/静态库/运行期日志,与 zip 绿色包口径一致
Source: "{#MyAppPublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.lib,logs\*"; Flags: ignoreversion recursesubdirs createallsubdirs

; 覆盖安装/应用内自更新(静默带 /DIR)前,由下方 [Code] 的 CleanStalePayload 清掉
; 上一版留在安装目录里的旧载荷(Inno 只覆盖同名文件、从不删旧文件;旧原生库残留
; 可能被 LoadLibrary 优先命中,导致升级后启动崩溃或行为异常)。
; 该函数保留 {app}\logs\ 与 unins000.*,详见函数注释。

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\McKuro"
; Windows 下运行期日志写在 {app}\logs(见 AppServices.LogDir),Inno 只删自己装过的文件,
; 不声明这里会残留一个 logs\ 目录(原版 1.3.8.4 实测如此)。既然卸载已清 %APPDATA%\McKuro
; 用户数据,日志也一并清掉,避免卸载后留空壳目录。
Type: filesandordirs; Name: "{app}\logs"

[Code]
// 安装目录默认值:优先 McKuro 启动时自注册的当前安装目录(HKCU,覆盖 zip 便携版无卸载键的场景,
// 见 src\McKuro\Services\InstallLocationRegistry.cs);目录已不存在或从未注册则回退 Program Files。
// 经安装器装过的场景由 Inno 原生 UsePreviousAppDir(默认 yes)直接复用旧目录,优先级更高,不走这里。
function DefaultDir(Param: String): String;
var
  Registered: String;
begin
  if RegQueryStringValue(HKCU, 'Software\McKuro', 'InstallPath', Registered)
     and (Registered <> '') and DirExists(Registered) then
    Result := Registered
  else
    Result := ExpandConstant('{autopf}\McKuro');
end;

// ---------------------------------------------------------------------------
// 覆盖安装 / 应用内自更新(/VERYSILENT /DIR=当前目录)前,清掉旧版留在 {app} 的载荷。
//
// 为什么必须清:Inno 只覆盖同名文件,不删旧文件。旧版替换掉的原生库(例如早期用过
// LibVLC 的 libvlc.dll / libvlc\ 目录)会一直留在 {app};McKuro 用 LoadLibrary 加载
// 原生库(NativeLibrary.TryLoad 默认搜索程序目录),残留旧库可能被优先命中,
// 导致升级后启动崩溃或行为异常。
//
// 保留清单(不能删):
//   logs\        —— Windows 下 McKuro 的文件日志就写在 {app}\logs(见 AppServices.LogDir)
//   unins000.*   —— 卸载器,删了会导致升级后无法卸载
//   *.log        —— setup 自身日志
// 用户配置在 %APPDATA%\McKuro(见 AppServices.AppDataDir)、WebView2 缓存在
// %LOCALAPPDATA%\McKuro,都不在 {app},清目录不影响用户数据。
// ---------------------------------------------------------------------------
function IsUserDataDir(const Name: String): Boolean;
begin
  Result := (CompareText(Name, 'logs') = 0)
            or (CompareText(Name, 'unins000.exe') = 0)
            or (CompareText(Name, 'unins000.dat') = 0);
end;

function IsUserDataFile(const Name: String): Boolean;
var
  Ext: String;
begin
  Ext := Lowercase(ExtractFileExt(Name));
  Result := (CompareText(Name, 'unins000.exe') = 0)
            or (CompareText(Name, 'unins000.dat') = 0)
            or (Ext = '.log');
end;

// 探测安装目录里是否残留了 .NET host 骨架(hostfxr.dll 等)。仅在日志里报告,
// 便于排查"升级后行为异常"是不是旧混合载荷导致的;清理本身不依赖它
// (CleanStalePayload 会统一删除所有非用户文件)。
function HasDotnetHostLeftover(const Dir: String): Boolean;
begin
  Result := FileExists(Dir + '\hostfxr.dll')
            or FileExists(Dir + '\hostpolicy.dll')
            or FileExists(Dir + '\coreclr.dll');
end;

// 返回删除的条目数。
// 触发条件:安装目录里已有 McKuro.exe(说明是覆盖旧版本/应用内自更新),
// 全新安装时目录里没有 exe,直接跳过,不会误删用户预置的文件。
// 删除失败的条目(如文件被占用)静默跳过,不影响安装继续。
procedure CleanStalePayload();
var
  FindRec: TFindRec;
  Dir, Name, Full: String;
  Count: Integer;
begin
  Dir := ExpandConstant('{app}');
  if not FileExists(Dir + '\McKuro.exe') then Exit;
  if HasDotnetHostLeftover(Dir) then
    Log('注意:安装目录残留 .NET host 骨架(hostfxr.dll 等),本次会一并清理');
  Count := 0;
  if FindFirst(Dir + '\*', FindRec) then
  try
    repeat
      Name := FindRec.Name;
      if (Name <> '.') and (Name <> '..') then
      begin
        Full := Dir + '\' + Name;
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          if not IsUserDataDir(Name) then
          begin
            if DelTree(Full, True, True, True) then Inc(Count);
          end;
        end
        else
        begin
          if not IsUserDataFile(Name) then
          begin
            if DeleteFile(Full) then Inc(Count);
          end;
        end;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
  if Count > 0 then
    Log('清理旧版载荷:删除 ' + IntToStr(Count) + ' 项(保留 logs\ 与 unins000.*)');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  // 覆盖安装/自更新:先清旧载荷,再做其余检查。顺序不能反——旧库残留会影响后续判断。
  CleanStalePayload();
end;

// 卸载收尾:删掉 {app}\logs 后,InstallDelete 已执行完、但 {app} 自身可能留成空壳目录
// (Inno 先删 {app} 再跑 UninstallDelete,顺序由它决定,我们无法插队)。
// 这里在 usPostUninstall 阶段兜底:目录为空就整个删掉。
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
  FindRec: TFindRec;
  Empty: Boolean;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Dir := ExpandConstant('{app}');
    if not DirExists(Dir) then Exit;
    // 只在目录已经空掉时才删,避免误删用户自行放进来的文件
    Empty := True;
    if FindFirst(Dir + '\*', FindRec) then
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Empty := False;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
    if Empty then
      RemoveDir(Dir);
  end;
end;
