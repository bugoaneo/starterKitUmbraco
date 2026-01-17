@echo off
echo Starting Sass Watch Mode for core.scss...
echo Press Ctrl+C to stop
cd /d %~dp0
call npm run sass:watch
