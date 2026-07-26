; ============================================================================
;  VideoRenamer - Inno Setup 安装脚本
;  由 打包安装程序.ps1 调用，或直接用 Inno Setup 打开编译。
;  预处理变量 AppVersion 可由命令行覆盖： ISCC.exe /DAppVersion=1.0.6.0 installer.iss
; ============================================================================

#define AppName "VideoRenamer"
#define AppPublisher "@寒松"
#define AppExeName "VideoRenamer.exe"
#ifndef AppVersion
  #define AppVersion "1.0.8.0"
#endif

[Setup]
; AppId 唯一标识本程序（升级/卸载靠它匹配），请勿修改。
AppId={{A7E4C1F2-3B8D-4E6A-9F0C-1D2E3B4A5C6D}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\VideoRenamer
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; 输出到项目的 installer 目录
OutputDir=installer
OutputBaseFilename=VideoRenamer-Setup-v{#AppVersion}
SetupIconFile=assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 64 位应用装到 64 位 Program Files；此程序 anycpu，用兼容架构即可
ArchitecturesInstallIn64BitMode=x64compatible
; 程序内含中文，界面默认中文
ShowLanguageDialog=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "assets\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 主程序（已内置 FFmpeg 资源），来自 dist\
Source: "dist\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; 快捷方式固定引用共享图标代理；目录授予标准用户修改权，程序每次启动只替换此文件内容。
Source: "assets\startup-icons\01.ico"; DestDir: "{commonappdata}\VideoRenamer\startup-icons"; DestName: "current.ico"; Flags: ignoreversion
; 可选随附文档（存在才打包）
Source: "dist\README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "dist\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Dirs]
Name: "{commonappdata}\VideoRenamer\startup-icons"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{commonappdata}\VideoRenamer\startup-icons\current.ico"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{commonappdata}\VideoRenamer\startup-icons\current.ico"; Tasks: desktopicon

[Run]
; 安装完成后可选立即运行
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; 说明：卸载时故意不删除 {localappdata}\VideoRenamer（授权/状态数据），
; 以便用户重装后无需重新激活。如需彻底清除，请手动删除该目录。
