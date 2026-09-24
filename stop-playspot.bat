@echo off
chcp 65001 >nul
title PlaySpot Stopper
cls
echo =====================================================================
echo                 🛑 إيقاف جميع خوادم PlaySpot 🛑
echo =====================================================================
echo.
echo  [+] جاري إيقاف الخوادم والأنفاق...

taskkill /f /fi "WINDOWTITLE eq PlaySpot*" >nul 2>&1
taskkill /f /im cloudflared.exe >nul 2>&1

echo.
echo  ✅ تم إيقاف جميع العمليات بنجاح.
timeout /t 2 >nul
exit
