### Plugin
- Updated .NET Packages
- Render StreamDock Knob feedback locally from the canvas layout and send the composed PNG with setImage.

### Profile Manager
- Import StreamDeck 1.x/2.x/3.x scenes as native StreamDock .SDProfile files, including scenes inside .ppp packages.
- Select N4, N4Pro, or XL as the scene target device. Preserve actions, settings, images, folder links, and page order; split pages that exceed the target key capacity.
- Map N4/N4Pro keypad and knob layouts; support XL's 32 keys and report incompatible knob actions before installation.
- Fix device assignment for keypad-only scenes, wrapped profile archives, and empty or all-zero current page IDs.
- Complete imports and old-profile replacement without stopping or restarting StreamDock.
- Fix profile renaming, mapping saves, device-list recovery, and installer task cleanup.

### Installer
- Set .NET 10.0.13 as Target
- Discover custom StreamDock installation paths through the registered protocol and correct shutdown waiting.
