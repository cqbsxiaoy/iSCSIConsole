set scriptPath=%~dp0
set ilmergeExe=%scriptPath%..\..\tools\ILMerge.3.0.41\tools\net452\ILMerge.exe
IF NOT EXIST "%ilmergeExe%" IF ["%programfiles(x86)%"]==[""] SET ilmergeExe=%programfiles%\Microsoft\ILMerge\ilmerge.exe
IF NOT EXIST "%ilmergeExe%" IF NOT ["%programfiles(x86)%"]==[""] SET ilmergeExe=%programfiles(x86)%\Microsoft\ILMerge\ilmerge.exe

set TargetFramework=%1
if %TargetFramework%==net20 set TargetPlaform=2.0
if %TargetFramework%==net40 set TargetPlaform=4.0
if %TargetFramework%==net472 set TargetPlaform=4.0
set binaryPath=%scriptPath%..\bin\Release\%TargetFramework%
set outputPath=%scriptPath%..\bin\ILMerge\%TargetFramework%
IF NOT EXIST "%outputPath%" MKDIR "%outputPath%"
set inputFiles="%binaryPath%\ISCSIConsole.exe" "%binaryPath%\Utilities.dll" "%binaryPath%\DiskAccessLibrary.dll" "%binaryPath%\DiskAccessLibrary.Win32.dll" "%binaryPath%\ISCSI.dll"
IF EXIST "%binaryPath%\DiscUtils.Core.dll" set inputFiles=%inputFiles% "%binaryPath%\DiscUtils.Core.dll"
IF EXIST "%binaryPath%\DiscUtils.Streams.dll" set inputFiles=%inputFiles% "%binaryPath%\DiscUtils.Streams.dll"
IF EXIST "%binaryPath%\DiscUtils.Vhdx.dll" set inputFiles=%inputFiles% "%binaryPath%\DiscUtils.Vhdx.dll"
"%ilmergeExe%" /targetplatform=%TargetPlaform% /ndebug /target:winexe /out:"%outputPath%\ISCSIConsole.exe" %inputFiles%
