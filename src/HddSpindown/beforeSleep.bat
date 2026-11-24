@echo off
:: Runs before a drive is locked/spun down, once per volume.
:: The app waits until this script has finished (max. 2 minutes).
::
::   %1  Drive letter, e.g. "D:" (empty if the volume has none)
::   %2  Name of the drive
::   %3  Device path
::
:: Example: stop a service that accesses the drive
::   if /i "%~1"=="D:" net stop "MyService"
