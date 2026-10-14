@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set LOG=claude-check.log
echo === dotnet restore === > %LOG%
dotnet restore "Yanyun.slnx" >> %LOG% 2>&1
echo RESTORE_EXIT=%ERRORLEVEL% >> %LOG%
echo === dotnet build === >> %LOG%
dotnet build "Yanyun.slnx" -c Release --no-restore >> %LOG% 2>&1
echo BUILD_EXIT=%ERRORLEVEL% >> %LOG%
echo === dotnet test === >> %LOG%
dotnet test "Yanyun.slnx" -c Release --no-build >> %LOG% 2>&1
echo TEST_EXIT=%ERRORLEVEL% >> %LOG%
echo === ef pending model changes === >> %LOG%
dotnet tool restore >> %LOG% 2>&1
if defined CLAUDE_MIGRATION dotnet ef migrations add %CLAUDE_MIGRATION% --project "apps/api/Yanyun.Api.csproj" --output-dir Migrations >> %LOG% 2>&1
if defined CLAUDE_MIGRATION echo MIGRATION_ADD_EXIT=%ERRORLEVEL% >> %LOG%
dotnet ef migrations has-pending-model-changes --project "apps/api/Yanyun.Api.csproj" >> %LOG% 2>&1
echo EF_EXIT=%ERRORLEVEL% >> %LOG%
echo === regenerate api types === >> %LOG%
set ASPNETCORE_ENVIRONMENT=Development
set Scheduling__Enabled=false
start "" /b dotnet apps\api\bin\Release\net10.0\Yanyun.Api.dll --contentRoot "%CD%\apps\api" --urls http://localhost:5099 > claude-api.log 2>&1
set OPENAPI_OK=0
for /l %%i in (1,1,40) do (
  if "!OPENAPI_OK!"=="0" (
    curl.exe --fail --silent http://localhost:5099/openapi/v1.json -o openapi.tmp.json && set OPENAPI_OK=1
    if "!OPENAPI_OK!"=="0" timeout /t 1 /nobreak >nul
  )
)
for /f "tokens=5" %%p in ('netstat -ano ^| findstr :5099 ^| findstr LISTENING') do taskkill /f /pid %%p >nul 2>&1
echo OPENAPI_OK=%OPENAPI_OK% >> %LOG%
if "%OPENAPI_OK%"=="1" call npx openapi-typescript openapi.tmp.json -o lib\api.generated.ts >> %LOG% 2>&1
del /q openapi.tmp.json 2>nul
echo === frontend === >> %LOG%
call npm run typecheck >> %LOG% 2>&1
echo TYPECHECK_EXIT=%ERRORLEVEL% >> %LOG%
call node --test tests/frontend-api.test.mjs >> %LOG% 2>&1
echo NODE_TEST_EXIT=%ERRORLEVEL% >> %LOG%
call npm run build >> %LOG% 2>&1
echo NEXT_EXIT=%ERRORLEVEL% >> %LOG%
echo ALL_DONE >> %LOG%
