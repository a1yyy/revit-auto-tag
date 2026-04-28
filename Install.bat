@echo off
:: RevitToolkit — Installer launcher
:: Double-click this file to install. No admin rights required.
::
:: This calls Install.ps1 with ExecutionPolicy Bypass, which only affects
:: this single script run and does not change any system-wide settings.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
