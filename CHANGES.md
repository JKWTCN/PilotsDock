### Plugin
- Updated SimConnect SDK source binaries (requires rebuilt local CFIT packages).

### Installer
- Set FSUIPC to Version 7.5.9.

### Build workflow
- Added a one-command Release.ps1 workflow to build local CFIT packages in dependency order, update all CFIT references, restore dependencies, publish applications, and package the installer.
- Verify published CFIT DLLs, dependency manifests, installer payload hashes, and installer version.
- Pack native SimConnect.dll as a win-x64 runtime asset to avoid managed assembly metadata warnings.
