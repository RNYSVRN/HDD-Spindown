@echo off
:: Runs after a drive has been woken up, once per volume.
::
::   %1  Drive letter, e.g. "D:" (empty if the volume has none)
::   %2  Name of the drive
::   %3  Device path
::
:: Example: start a service again
::   if /i "%~1"=="D:" net start "MyService"
