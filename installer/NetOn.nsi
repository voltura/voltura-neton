Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "StrFunc.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
${StrStr}
!ifndef APP_VERSION
  !error "Build with scripts/build-release.ps1"
!endif
!ifndef UNINSTALL_KEY
  !define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn"
!endif
!ifndef INSTALLER_KEY
  !define INSTALLER_KEY "Software\Voltura\NetOn\Installer"
!endif
!ifndef MENU_NAME
  !define MENU_NAME "Voltura NetOn"
!endif
Name "Voltura NetOn"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\Voltura NetOn"
InstallDirRegKey HKCU "${INSTALLER_KEY}" "InstallDir"
RequestExecutionLevel user
ManifestDPIAware true
ManifestDPIAwareness PerMonitorV2,PerMonitor
ManifestSupportedOS Win10
SetFont "Segoe UI" 9
BrandingText "Voltura AB"
SetCompressor /SOLID lzma
SetOverwrite on
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "Voltura NetOn"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "CompanyName" "Voltura AB"
VIAddVersionKey "FileDescription" "Voltura NetOn installer"
VIAddVersionKey "FileVersion" "${APP_VERSION}.0"
VIAddVersionKey "LegalCopyright" "Voltura AB"
!define MUI_ICON "..\appicon.ico"
!define MUI_UNICON "..\appicon.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "art\wizard.bmp"
!define MUI_WELCOMEFINISHPAGE_BITMAP_STRETCH FitControl
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP "art\header.bmp"
!define MUI_HEADERIMAGE_BITMAP_STRETCH FitControl
!define MUI_BGCOLOR "F7F9F5"
!define MUI_TEXTCOLOR "20352B"
!define MUI_WELCOMEPAGE_TITLE "Welcome to NetOn"
!define MUI_WELCOMEPAGE_TEXT "Internet on or off, whenever you choose.$\r$\n$\r$\nSetup will add NetOn to your PC.$\r$\n$\r$\nClick Next to get started."
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TITLE "You're all set."
!define MUI_FINISHPAGE_TEXT "Open Voltura NetOn from the Start menu.$\r$\n$\r$\nClick its icon next to the clock to turn internet on or off. Right-click for settings."
!define MUI_FINISHPAGE_RUN "$INSTDIR\Voltura.NetOn.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Start NetOn"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
Var RuntimeRoot
Var RuntimeFound
Var RuntimeExitCode
Var HadRunningNetOn

Function ClosePreviousVersion
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  !ifdef TEST_UPGRADE_HELPER
    File "/oname=stop-previous-version.ps1" "${TEST_UPGRADE_HELPER}"
  !else
    File "/oname=stop-previous-version.ps1" "..\scripts\stop-previous-version.ps1"
  !endif
  nsExec::ExecToLog '$\"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe$\" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $\"$PLUGINSDIR\stop-previous-version.ps1$\" -InstallingVersion ${APP_VERSION}.0'
  Pop $0
  ${If} $0 == 10
    StrCpy $HadRunningNetOn 1
  ${ElseIf} $0 != 0
    IfSilent close_failed
    MessageBox MB_OK|MB_ICONEXCLAMATION "Setup could not close the running NetOn. Exit it from its tray menu and try again."
close_failed:
    SetErrorLevel 1618
    Quit
  ${EndIf}
FunctionEnd

Function .onInstSuccess
  IfSilent 0 start_done
  ${If} $HadRunningNetOn == 1
    Exec '$\"$INSTDIR\Voltura.NetOn.exe$\"'
  ${EndIf}
start_done:
FunctionEnd
Function CheckDesktopRuntime
  StrCpy $RuntimeFound 0
  FindFirst $0 $1 "$RuntimeRoot\shared\Microsoft.WindowsDesktop.App\10.0.*"
runtime_loop:
  StrCmp $1 "" runtime_done
  ${StrStr} $2 $1 "-"
  StrCmp $2 "" 0 runtime_next
  IfFileExists "$RuntimeRoot\shared\Microsoft.WindowsDesktop.App\$1\System.Windows.Forms.dll" 0 runtime_next
  StrCpy $RuntimeFound 1
  Goto runtime_done
runtime_next:
  FindNext $0 $1
  Goto runtime_loop
runtime_done:
  FindClose $0
FunctionEnd
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "Voltura NetOn requires 64-bit Windows."
    SetErrorLevel 1633
    Quit
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "Voltura NetOn requires Windows 10 or later."
    SetErrorLevel 1633
    Quit
  ${EndIf}
  SetRegView 64
  SetShellVarContext current
  !ifdef TEST_RUNTIME_ROOT
    StrCpy $RuntimeRoot "${TEST_RUNTIME_ROOT}"
  !else
    ReadRegStr $RuntimeRoot HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64" "InstallLocation"
    ${If} $RuntimeRoot == ""
      StrCpy $RuntimeRoot "$PROGRAMFILES64\dotnet"
    ${EndIf}
  !endif
  Call CheckDesktopRuntime
FunctionEnd

Function EnsureDesktopRuntime
  ${If} $RuntimeFound == 1
    Return
  ${EndIf}
  DetailPrint "Setting up required Windows components..."
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  !ifdef TEST_RUNTIME_HELPER
    File "/oname=install-runtime.ps1" "${TEST_RUNTIME_HELPER}"
  !else
    File "/oname=install-runtime.ps1" "..\scripts\install-runtime.ps1"
  !endif
  nsExec::ExecToLog '$\"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe$\" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $\"$PLUGINSDIR\install-runtime.ps1$\"'
  Pop $RuntimeExitCode
  ${If} $RuntimeExitCode == 1223
    IfSilent runtime_canceled
    MessageBox MB_OK|MB_ICONINFORMATION "Setup was canceled. NetOn has not been installed."
runtime_canceled:
    SetErrorLevel 1223
    Quit
  ${EndIf}
  ${If} $RuntimeExitCode != 0
  ${AndIf} $RuntimeExitCode != 3010
    IfSilent runtime_failed
    MessageBox MB_OK|MB_ICONSTOP "Setup could not finish. Check your internet connection and try again."
runtime_failed:
    SetErrorLevel 1603
    Quit
  ${EndIf}
  Call CheckDesktopRuntime
  ${If} $RuntimeFound != 1
    IfSilent runtime_unavailable
    MessageBox MB_OK|MB_ICONINFORMATION "Restart Windows, then run Setup again to finish installing NetOn."
runtime_unavailable:
    SetErrorLevel 3010
    Quit
  ${EndIf}
  ${If} $RuntimeExitCode == 3010
    SetRebootFlag true
  ${EndIf}
FunctionEnd
!macro CheckClosed PREFIX
  IfFileExists "$INSTDIR\Voltura.NetOn.exe" 0 ${PREFIX}ready
${PREFIX}retry:
  System::Call 'kernel32::CreateFileW(w "$INSTDIR\Voltura.NetOn.exe", i 0x40000000, i 7, p 0, i 3, i 0, p 0) p .r0'
  ${If} $0 == -1
    IfSilent ${PREFIX}inuse
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Exit NetOn using its tray menu before continuing." IDRETRY ${PREFIX}retry
${PREFIX}inuse:
    SetErrorLevel 1618
    Quit
  ${EndIf}
  System::Call 'kernel32::CloseHandle(p r0)'
${PREFIX}ready:
!macroend
Section "Voltura NetOn (required)" MainSection
  SectionIn RO
  Call EnsureDesktopRuntime
  Call ClosePreviousVersion
  !insertmacro CheckClosed "install_"
  !include "${INSTALL_FILES}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${INSTALLER_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Voltura NetOn"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "Voltura AB"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Voltura.NetOn.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "URLInfoAbout" "https://github.com/voltura/voltura-neton"
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" $0
  CreateDirectory "$SMPROGRAMS\${MENU_NAME}"
  CreateShortcut "$SMPROGRAMS\${MENU_NAME}\Voltura NetOn.lnk" "$INSTDIR\Voltura.NetOn.exe"
  CreateShortcut "$SMPROGRAMS\${MENU_NAME}\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd
Section /o "Desktop shortcut" DesktopSection
  CreateShortcut "$DESKTOP\Voltura NetOn.lnk" "$INSTDIR\Voltura.NetOn.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DesktopShortcut" "$INSTDIR"
SectionEnd
Function un.onInit
  SetRegView 64
  SetShellVarContext current
FunctionEnd
Section "Uninstall"
  !insertmacro CheckClosed "uninstall_"
  !include "${UNINSTALL_FILES}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${MENU_NAME}\Voltura NetOn.lnk"
  Delete "$SMPROGRAMS\${MENU_NAME}\Uninstall.lnk"
  RMDir "$SMPROGRAMS\${MENU_NAME}"
  ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "DesktopShortcut"
  ${If} $0 == "$INSTDIR"
    Delete "$DESKTOP\Voltura NetOn.lnk"
  ${EndIf}
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Voltura NetOn"
  ${If} $0 == '$\"$INSTDIR\Voltura.NetOn.exe$\"'
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Voltura NetOn"
  ${EndIf}
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegKey HKCU "${INSTALLER_KEY}"
SectionEnd
