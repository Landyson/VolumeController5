@echo off
setlocal
cd /d "%~dp0..\pc-app\VolumeController5" || (pause & exit /b 1)
dotnet restore || (pause & exit /b 1)
dotnet run
