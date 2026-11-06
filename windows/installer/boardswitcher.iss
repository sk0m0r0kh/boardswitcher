; Установщик boardswitcher для Windows (Inno Setup 6).
; Сборка: ..\build.ps1 (сначала собирает exe, потом этот установщик).

#define AppName "boardswitcher"
#define AppVersion "1.2.0"
#define AppExe "boardswitcher.exe"
#define DotNetUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
#define DotNetFile "windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{8C3B2E4A-5D1F-4E6B-9A7C-2F1E0D3B4A5C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Dmitrii Markov
AppPublisherURL=https://github.com/sk0m0r0kh/boardswitcher
AppSupportURL=https://github.com/sk0m0r0kh/boardswitcher
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=Output
OutputBaseFilename=boardswitcher-setup-{#AppVersion}
SetupIconFile=..\boardswitcher.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
UsePreviousTasks=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"
Name: "autostart"; Description: "Запускать при входе в Windows"; GroupDescription: "Запуск:"
Name: "admin"; Description: "Запускать с правами администратора (чтобы программа работала и в окнах администратора)"; GroupDescription: "Запуск:"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Caps Lock меняет раскладку слова или выделенного текста"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Автозапуск и права администратора: задача Планировщика для пользователя.
; С правами администратора задачу создаёт сам установщик (у него эти права есть),
; без них — от имени пользователя, который запустил установку.
Filename: "{app}\{#AppExe}"; Parameters: "--apply-task {code:TaskFlag|autostart} 1"; Tasks: admin; Flags: runhidden waituntilterminated; StatusMsg: "Настройка запуска..."
Filename: "{app}\{#AppExe}"; Parameters: "--apply-task {code:TaskFlag|autostart} 0"; Tasks: not admin; Flags: runhidden waituntilterminated runasoriginaluser; StatusMsg: "Настройка запуска..."
Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppName}"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "boardswitcher-uninstall"

[UninstallDelete]
Type: files; Name: "{%USERPROFILE}\boardswitcher.log"

[Code]
var
  DownloadPage: TDownloadWizardPage;

function TaskFlag(Param: String): String;
begin
  if WizardIsTaskSelected(Param) then
    Result := '1'
  else
    Result := '0';
end;

{ .NET 10 Desktop Runtime установлен? }
function IsDotNetDesktop10Installed(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

{ Файл подписан действительной подписью Microsoft Corporation? }
function IsMicrosoftSigned(const FileName: String): Boolean;
var
  ResultCode: Integer;
  Params: String;
begin
  Params := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' +
    '$s = Get-AuthenticodeSignature -LiteralPath ''' + FileName + '''; ' +
    'if ($s.Status -eq ''Valid'' -and $s.SignerCertificate.Subject -match ''O=Microsoft Corporation'') { exit 0 } else { exit 1 }"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params, '', SW_HIDE,
                 ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure KillRunning();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage('Установка .NET 10', 'Для работы программы нужен .NET 10 Desktop Runtime. Он будет скачан с сайта Microsoft и установлен.', nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID = wpReady) and not IsDotNetDesktop10Installed() then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('{#DotNetUrl}', '{#DotNetFile}', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        DownloadPage.SetText('Проверка подписи .NET 10 Desktop Runtime...', '');
        DownloadPage.ProgressBar.Style := npbstMarquee;
        if not IsMicrosoftSigned(ExpandConstant('{tmp}\{#DotNetFile}')) then
        begin
          MsgBox('Скачанный установщик .NET не подписан Microsoft. Установка прервана.' + #13#10 +
                 'Установите .NET 10 Desktop Runtime вручную: https://dotnet.microsoft.com/download/dotnet/10.0', mbError, MB_OK);
          Result := False;
          Exit;
        end;
        DownloadPage.SetText('Установка .NET 10 Desktop Runtime...', 'Это может занять минуту.');
        if not Exec(ExpandConstant('{tmp}\{#DotNetFile}'), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
           or ((ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1638)) then
        begin
          MsgBox('Не удалось установить .NET 10 Desktop Runtime (код ' + IntToStr(ResultCode) + ').' + #13#10 +
                 'Установите его вручную: https://dotnet.microsoft.com/download/dotnet/10.0', mbError, MB_OK);
          Result := False;
        end;
      except
        if not DownloadPage.AbortedByUser then
          SuppressibleMsgBox('Не удалось скачать .NET 10 Desktop Runtime: ' + GetExceptionMessage + #13#10 +
                             'Проверьте подключение к интернету.', mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { закрыть запущенную программу, чтобы заменить exe }
  KillRunning();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  KillRunning();
  Result := True;
end;
