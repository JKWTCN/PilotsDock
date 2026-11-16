### Plugin
- Sync PilotsDeck 0.9.8.0 and updated CFIT dependencies and SimConnect SDK.
- Initialize global settings, profile mappings, and scripts before starting the refresh timer; wait for global settings before refreshing simulator state.
- Fall back when setting Windows efficiency mode fails.
- Return safely when a requested global script is unavailable.
- Preserve the local StreamDock device, knob feedback, and profile import adaptations.

### Installer
- Set .NET 10.0.15 as Target.
- Preserve custom StreamDock discovery and installation behavior.
