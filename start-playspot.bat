@echo off
chcp 65001 >nul
title PlaySpot Launcher
cls
echo =====================================================================
echo                 🏆 PlaySpot / CourtBook Launcher 🏆
echo =====================================================================
echo.
echo  اختر طريقة التشغيل:
echo.
echo  [1] تشغيل محلي فقط (Localhost - على هذا الجهاز)
echo  [2] تشغيل مع رابط خارجي للموبايل والإنترنت (Cloudflare Tunnel)
echo  [3] إيقاف تشغيل الخوادم (Stop All)
echo.
set /p choice="أدخل اختيارك (1 أو 2 أو 3): "

if "%choice%"=="1" goto LOCAL
if "%choice%"=="2" goto TUNNEL
if "%choice%"=="3" goto STOP
goto LOCAL

:LOCAL
echo.
echo  [+] جاري تشغيل خادم الـ API على المنفذ 5257...
start "PlaySpot API" /min cmd /c "cd /d C:\Users\Mohamed\source\repos\CourtBook && dotnet run --project src/CourtBook.API --launch-profile http"

echo  [+] جاري تشغيل موقع الويب على المنفذ 5100...
start "PlaySpot Web" /min cmd /c "cd /d C:\Users\Mohamed\source\repos\CourtBook && dotnet run --project src/CourtBook.Web --launch-profile http"

echo.
echo  انتظر 5 ثوانٍ حتى تكتمل جاهزية الخوادم...
timeout /t 5 /nobreak >nul

echo  [+] جاري فتح الموقع في المتصفح...
start http://localhost:5100
start http://localhost:5257/swagger

echo.
echo =====================================================================
echo  ✅ تم التشغيل بنجاح!
echo  - الموقع: http://localhost:5100
echo  - واجهة Swagger: http://localhost:5257/swagger
echo.
echo  (لإيقاف التشغيل في أي وقت، يمكنك تشغيل ملف "إيقاف المشروع")
echo =====================================================================
pause
exit

:TUNNEL
echo.
echo  [+] جاري تشغيل خادم الـ API على المنفذ 5257...
start "PlaySpot API" /min cmd /c "cd /d C:\Users\Mohamed\source\repos\CourtBook && dotnet run --project src/CourtBook.API --launch-profile http"

echo  [+] جاري تشغيل موقع الويب على المنفذ 5100...
start "PlaySpot Web" /min cmd /c "cd /d C:\Users\Mohamed\source\repos\CourtBook && dotnet run --project src/CourtBook.Web --launch-profile http"

echo  [+] جاري إنشاء نفق سحابي للموقع (Web Tunnel)...
start "PlaySpot Web Tunnel" cmd /k "C:\Users\Mohamed\cloudflared.exe tunnel --url http://localhost:5100"

echo  [+] جاري إنشاء نفق سحابي للـ API (API Tunnel)...
start "PlaySpot API Tunnel" cmd /k "C:\Users\Mohamed\cloudflared.exe tunnel --url http://localhost:5257"

echo.
echo  انتظر 5 ثوانٍ حتى تكتمل جاهزية الخوادم...
timeout /t 5 /nobreak >nul

echo  [+] جاري فتح الموقع المحلي في المتصفح...
start http://localhost:5100

echo.
echo =====================================================================
echo  ✅ تم التشغيل مع النفق الخارجي بنجاح!
echo.
echo  ستظهر لك نافذتان باللون الأسود للنفق السحابي:
echo  - ستجد داخل كل نافذة رابط يبدأ بـ https://xxx.trycloudflare.com
echo  - يمكنك نسخ هذا الرابط وفتحه مباشرة من الموبايل في أي وقت!
echo =====================================================================
pause
exit

:STOP
echo.
echo  [+] جاري إيقاف جميع العمليات التابعة لـ PlaySpot...
taskkill /f /fi "WINDOWTITLE eq PlaySpot*" >nul 2>&1
taskkill /f /im cloudflared.exe >nul 2>&1
echo  ✅ تم إيقاف جميع الخوادم بنجاح.
timeout /t 2 >nul
exit
