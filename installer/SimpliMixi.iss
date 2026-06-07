#define MyAppName "SimpliMixi"
#define MyAppVersion "0.6.1"
#define MyAppPublisher "SimpliMixi"
#define MyAppExeName "SimpliMixi.exe"
#define MyAppId "9D2F0D65-A778-4F3D-8C08-84DBF9165F57"
#define SourceDir "..\publish\SimpliMixi-v0.6.1"
#define DotNetRuntimeInstaller "windowsdesktop-runtime-8.0.0-win-x64.exe"

[Setup]
AppId={{{#MyAppId}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
DirExistsWarning=no
OutputDir=..\publish
OutputBaseFilename=SimpliMixi-v{#MyAppVersion}-Setup
SetupIconFile=..\Assets\AppIcon\SimpliMixi.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\redist\{#DotNetRuntimeInstaller}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not IsDotNetDesktopRuntime8Installed

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\{#DotNetRuntimeInstaller}"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing Microsoft .NET 8 Desktop Runtime..."; Check: not IsDotNetDesktopRuntime8Installed
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: ShouldLaunchAppAfterUpdate

[Code]
function ShouldLaunchAppAfterUpdate(): Boolean;
begin
  Result := Pos('/LAUNCHAPP=1', Uppercase(GetCmdTail())) > 0;
end;

procedure StopRunningProcess(ProcessName: String);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM ' + ProcessName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure StopRunningAppProcesses();
begin
  StopRunningProcess('{#MyAppExeName}');
  StopRunningProcess('adb.exe');
end;

function IsDotNetDesktopRuntime8Installed(): Boolean;
var
  Versions: TArrayOfString;
  I: Integer;
begin
  Result := False;

  if RegGetSubkeyNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Versions) then
  begin
    for I := 0 to GetArrayLength(Versions) - 1 do
    begin
      if Pos('8.', Versions[I]) = 1 then
      begin
        Result := True;
        exit;
      end;
    end;
  end;
end;

procedure DeleteOldVersionItemsInDir(Dir: String);
var
  FindRec: TFindRec;
  ItemPath: String;
begin
  if (Dir = '') or (not DirExists(Dir)) then
    exit;

  if FindFirst(AddBackslash(Dir) + '*SimpliMixi*0.5.0*', FindRec) then
  begin
    try
      repeat
        ItemPath := AddBackslash(Dir) + FindRec.Name;
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = FILE_ATTRIBUTE_DIRECTORY then
          DelTree(ItemPath, True, True, True)
        else
          DeleteFile(ItemPath);
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure DeleteOldVersionItems();
var
  UserProfile: String;
begin
  DeleteOldVersionItemsInDir(ExpandConstant('{src}'));
  DeleteOldVersionItemsInDir(ExpandConstant('{userdesktop}'));
  DeleteOldVersionItemsInDir(ExpandConstant('{commondesktop}'));

  UserProfile := GetEnv('USERPROFILE');
  if UserProfile <> '' then
    DeleteOldVersionItemsInDir(AddBackslash(UserProfile) + 'Downloads');
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  StopRunningAppProcesses();
  DeleteOldVersionItems();
end;
