@echo off
cd /d "%~dp0YardController.Web"
dotnet run --no-launch-profile --environment Production --urls "https://localhost:7216;http://localhost:5180"
