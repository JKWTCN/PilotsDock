# One-command release

After synchronizing the PilotsDock and CFIT sources, run in PowerShell 7.4 or newer:

```powershell
cd D:\code\PilotsDock
.\Release.ps1
```

The script uses the sibling `..\CFIT` checkout by default. It builds and packs
AppLogger, AppTools, SimConnectLib, AppFramework, and Installer in dependency
order with one new CFIT version. Each dependent CFIT project is updated before
its build, so the generated packages also declare the new dependency versions.
Legacy recursive CFIT build hooks are disabled only for this workflow.

It then updates the plugin, Profile Manager, and installer references, restores
packages from the local CFIT repository, synchronizes the SimConnect SDK files,
publishes all three application projects, and creates the installer. It verifies
the CFIT DLL hashes in the publish directory and installer payload, the published
dependency manifests, and the installer version. Native SimConnect.dll is packed
under `runtimes/win-x64/native`, rather than treated as a managed assembly.

The release version defaults to `Plugin/manifest.json`; the script updates
`Installer/Payload/version.json` and the installer assembly metadata for the new
build. The final output is `PilotsDock-Installer-latest.exe`.

```powershell
# Override the application version or CFIT checkout.
.\Release.ps1 -Version 0.9.6.0 -CfitRoot D:\code\CFIT

# Reuse existing local CFIT packages; still update/restore all app dependencies.
.\Release.ps1 -SkipCfitBuild
```

Requires the .NET 10 SDK, Visual Studio MSBuild with .NET Framework 4.8 build
support, PowerShell 7.4 or newer, and `C:\Program Files\7-Zip\7z.exe`. Missing external NuGet
packages require network access. The script changes dependency references and
release artifacts in both working trees. Git synchronization, commits, pushes,
and uploading a GitHub release remain separate operations.

The publish folder is fixed to `Releases/com.mirabox.pilotsdock.sdPlugin` because
the installer packs that directory. Concurrent runs are prevented by
`.release.lck`; the script removes its lock on success or failure. After a forced
process termination, remove that file only once the release process has stopped.
