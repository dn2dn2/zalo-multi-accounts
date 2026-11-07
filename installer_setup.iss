; Kịch bản đóng gói Inno Setup - Zalo Multi-Workspace Pro (Giai đoạn 5)
#define MyAppName "Zalo Multi-Workspace Pro"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ZaloMultiWorkspace"
#define MyAppExeName "ZaloMultiWorkspace.exe"

[Setup]
AppId={{8F9B3B45-2C8A-4D7E-9A1F-6B5C8E4D2A10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputBaseFilename=ZaloMultiWorkspacePro_Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "ZaloMultiWorkspace\bin\Release\net8.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Đặt bộ cài WebView2 Evergreen Bootstrapper vào thư mục redist nếu cần đóng gói kèm
; Source: "redist\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Code]
function IsWebView2RuntimeInstalled(): Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

[Run]
; Tự động cài đặt WebView2 Runtime nếu máy khách chưa có
Filename: "{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Parameters: "/silent /install"; StatusMsg: "Đang cài đặt Microsoft Edge WebView2 Runtime..."; Check: not IsWebView2RuntimeInstalled; Flags: waituntilterminated skipifdoesntexist
Filename: "{app}\{#MyAppExeName}"; Description: "Khởi động {#MyAppName}"; Flags: nowait postinstall skipifsilent

