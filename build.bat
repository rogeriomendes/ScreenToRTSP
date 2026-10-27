@echo off
echo.
echo ===========================================
echo       GERANDO EXECUTAVEL SCREENTORTSP
echo ===========================================
echo.

echo [1/3] Encerrando o aplicativo (se estiver aberto)...
taskkill /F /IM ScreenToRTSP.exe /T >nul 2>&1
taskkill /F /IM "ScreenToRTSP*" /T >nul 2>&1
ping 127.0.0.1 -n 3 > nul

echo [2/3] Compilando o projeto WinUI 3 em arquivo unico (Release)...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true

echo [3/3] Movendo o executavel pronto para a pasta 'build'...
if exist build rmdir /S /Q build
mkdir build
copy /Y "bin\Release\net10.0-windows10.0.22621.0\win-x64\publish\ScreenToRTSP.exe" "build\ScreenToRTSP.exe" >nul

echo.
echo ===========================================
echo       BUILD CONCLUIDO COM SUCESSO!
echo ===========================================
echo O executavel final esta na pasta 'build\ScreenToRTSP.exe'.
echo.
