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
Name "Voltura NetOn"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\Voltura NetOn"
InstallDirRegKey HKCU "Software\Voltura\NetOn\Installer" "InstallDir"
RequestExecutionLevel user
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
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "Voltura NetOn is installed.$\r$\n$\r$\nOpen it from the Start menu. Windows will request administrator permission once when NetOn starts."
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
Var RuntimeRoot
Var RuntimeFound
Var RuntimeExitCode
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
  !insertmacro CheckClosed "install_"
  Call EnsureDesktopRuntime
  !include "${INSTALL_FILES}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Voltura\NetOn\Installer" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "DisplayName" "Voltura NetOn"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "Publisher" "Voltura AB"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "DisplayIcon" "$INSTDIR\Voltura.NetOn.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "URLInfoAbout" "https://github.com/voltura/voltura-neton"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "EstimatedSize" $0
  CreateDirectory "$SMPROGRAMS\Voltura NetOn"
  CreateShortcut "$SMPROGRAMS\Voltura NetOn\Voltura NetOn.lnk" "$INSTDIR\Voltura.NetOn.exe"
  CreateShortcut "$SMPROGRAMS\Voltura NetOn\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
SectionEnd
Section /o "Desktop shortcut" DesktopSection
  CreateShortcut "$DESKTOP\Voltura NetOn.lnk" "$INSTDIR\Voltura.NetOn.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "DesktopShortcut" "$INSTDIR"
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
  Delete "$SMPROGRAMS\Voltura NetOn\Voltura NetOn.lnk"
  Delete "$SMPROGRAMS\Voltura NetOn\Uninstall.lnk"
  RMDir "$SMPROGRAMS\Voltura NetOn"
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn" "DesktopShortcut"
  ${If} $0 == "$INSTDIR"
    Delete "$DESKTOP\Voltura NetOn.lnk"
  ${EndIf}
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Voltura NetOn"
  ${If} $0 == '$\"$INSTDIR\Voltura.NetOn.exe$\"'
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Voltura NetOn"
  ${EndIf}
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura.NetOn"
  DeleteRegKey HKCU "Software\Voltura\NetOn\Installer"
SectionEnd
