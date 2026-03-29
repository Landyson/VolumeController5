@echo off
setlocal
cd /d "%~dp0..\pc-app\VolumeController5" || (pause & exit /b 1)

where dotnet || (echo DOTNET not found & pause & exit /b 1)
dotnet --version

if exist "bin\Release\net8.0-windows\win-x64\publish" rmdir /s /q "bin\Release\net8.0-windows\win-x64\publish"

dotnet restore || (pause & exit /b 1)
dotnet publish -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false || (pause & exit /b 1)

echo.
echo DONE. Compact EXE:
echo %CD%\bin\Release\net8.0-windows\win-x64\publish\VolumeController5.exe
echo.
echo Poznamka: tato kompaktni verze vyzaduje nainstalovany .NET Desktop Runtime 8 na cilovem PC.
pause
