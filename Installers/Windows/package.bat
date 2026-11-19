@rem Makes the installers' zips into the repository's Installers folder, run from any folder or by a double-click:
@rem package.ps1, beside this file, says what they are. Its arguments go on to it, -Destination <folder> among them.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0package.ps1" %*
