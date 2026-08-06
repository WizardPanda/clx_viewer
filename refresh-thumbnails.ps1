# Fully resets the Explorer thumbnail cache so .clx previews regenerate.
# Kills and restarts explorer.exe (the taskbar briefly disappears), then clears
# the thumbnail databases.
$ErrorActionPreference = "Stop"

Write-Host "Restarting Explorer and clearing the thumbnail cache..."
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$cache = Join-Path $env:LOCALAPPDATA "Microsoft\Windows\Explorer"
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $cache "thumbcache_*.db")
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $cache "iconcache_*.db")
Start-Process explorer.exe
Write-Host "Done. Explorer restarted and thumbnail cache cleared."
