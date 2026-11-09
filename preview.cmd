@echo off
cd /d "%~dp0"
python tools\make-preview.py
start "" "%~dp0README-preview.html"
