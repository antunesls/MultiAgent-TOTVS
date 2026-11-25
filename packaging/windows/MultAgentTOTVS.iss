#ifndef AppVersion
  #error "AppVersion obrigatoria"
#endif
#ifndef PayloadDir
  #error "PayloadDir obrigatorio"
#endif
#ifndef ReleaseDir
  #error "ReleaseDir obrigatorio"
#endif

[Setup]
AppId={{C0755DAC-16FB-489E-B69A-BA59FB175794}
AppName=MultAgent TOTVS
AppVersion={#AppVersion}
AppPublisher=MultAgent TOTVS
AppPublisherURL=https://github.com/antunesls/MultiAgent-TOTVS
AppSupportURL=https://github.com/antunesls/MultiAgent-TOTVS/issues
DefaultDirName={localappdata}\Programs\MultAgent TOTVS
DefaultGroupName=MultAgent TOTVS
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableWelcomePage=no
OutputDir={#ReleaseDir}
OutputBaseFilename=MultAgent-TOTVS-{#AppVersion}-win-x64-setup
UninstallDisplayIcon={app}\WebAgentManager.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=WebAgentManager.exe
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na area de trabalho"; Flags: unchecked
Name: "startup"; Description: "Iniciar o gerenciador no login deste usuario (sem abrir o painel)"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\MultAgent TOTVS"; Filename: "{app}\Iniciar.cmd"; WorkingDir: "{app}"; IconFilename: "{app}\WebAgentManager.exe"
Name: "{userdesktop}\MultAgent TOTVS"; Filename: "{app}\Iniciar.cmd"; WorkingDir: "{app}"; IconFilename: "{app}\WebAgentManager.exe"; Tasks: desktopicon
Name: "{userstartup}\MultAgent TOTVS"; Filename: "{app}\WebAgentManager.exe"; WorkingDir: "{app}"; Tasks: startup

[Run]
Filename: "{app}\Iniciar.cmd"; Description: "Abrir MultAgent TOTVS"; Flags: postinstall shellexec nowait skipifsilent unchecked

[Messages]
WelcomeLabel2=Este assistente instala o MultAgent TOTVS somente para seu usuario.%n%nAntes de atualizar, conclua ou cancele preparacoes de instalador e feche o gerenciador. Os WebAgents nao precisam ser encerrados para atualizar somente o gerenciador.%n%nProjeto independente, sem vinculo com a TOTVS. Instaladores e agentes TOTVS nao acompanham este pacote.
