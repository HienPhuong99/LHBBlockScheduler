@echo off
chcp 65001 >nul
echo ============================================
echo   LHB Block Scheduler - Build va Dong goi
echo ============================================
echo.
echo Dang build va cai dat plugin...
echo Vui long doi...
echo.

powershell -ExecutionPolicy Bypass -File "%~dp0build.ps1"

echo.
if %ERRORLEVEL% NEQ 0 (
    echo [LOI] Build that bai! Xem thong bao o tren de biet chi tiet.
) else (
    echo [OK] Da cai dat xong!
    echo.
    echo Buoc tiep theo:
    echo   1. Dong AutoCAD hoan toan ^(neu dang mo^)
    echo   2. Mo lai AutoCAD
    echo   3. Plugin se tu dong nap, khong can lam gi them
)

echo.
pause
