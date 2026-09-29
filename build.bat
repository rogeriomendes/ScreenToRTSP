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

echo [2/3] Compilando o projeto em um arquivo unico (Release)...
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true

echo [3/3] Movendo o executavel pronto para a pasta 'build'...
if not exist build mkdir build
copy /Y "bin\Release\net10.0-windows\win-x64\publish\ScreenToRTSP.exe" "build\ScreenToRTSP.exe"

echo.
echo ===========================================
echo       BUILD CONCLUIDO COM SUCESSO!
echo ===========================================
echo O executavel final esta na pasta 'build'.
echo.
