# EtwSuite MSI Installer

EtwSuite uses WiX Toolset to build MSI installers for the unpackaged WinUI app.

Build the x64 installer:

```powershell
dotnet build installer\EtwSuite.Installer\EtwSuite.Installer.wixproj -c Release -p:Platform=x64 -p:AcceptEula=wix7
```

Build the ARM64 installer:

```powershell
dotnet build installer\EtwSuite.Installer\EtwSuite.Installer.wixproj -c Release -p:Platform=ARM64 -p:AcceptEula=wix7
```

Only pass `AcceptEula=wix7` after confirming the WiX Toolset OSMF terms apply
appropriately for your use.

The installer wizard lets the user choose per-user or per-machine scope, choose
the install folder, and include or omit the Start Menu shortcut.

If the build environment cannot access the Windows Installer service during ICE
validation, build with validation suppressed:

```powershell
dotnet build installer\EtwSuite.Installer\EtwSuite.Installer.wixproj -c Release -p:Platform=x64 -p:AcceptEula=wix7 -p:SuppressValidation=true
```
