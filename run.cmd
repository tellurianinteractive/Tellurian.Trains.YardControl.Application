@echo off
cd /d "%~dp0YardController.Web"
dotnet run --no-launch-profile --environment Production
