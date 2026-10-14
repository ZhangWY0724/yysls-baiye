@echo off
cd /d "%~dp0"
echo === dotnet build === > claude-check.log
dotnet build "Yanyun.slnx" -c Release >> claude-check.log 2>&1
echo BUILD_EXIT=%ERRORLEVEL% >> claude-check.log
echo === dotnet test === >> claude-check.log
dotnet test "Yanyun.slnx" -c Release --no-build >> claude-check.log 2>&1
echo TEST_EXIT=%ERRORLEVEL% >> claude-check.log
echo === npm run build === >> claude-check.log
call npm run build >> claude-check.log 2>&1
echo NEXT_EXIT=%ERRORLEVEL% >> claude-check.log
echo ALL_DONE >> claude-check.log
