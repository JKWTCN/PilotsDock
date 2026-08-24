### Plugin
- Added Feature to assign separate Sim Commands for turning Dials while pressed
  - If not assigned, the Actions will behave like before (normal Rotate Commands are executed regardless if the Dial is pressed or not)
  - When assigned, it is not recommended to assign a Sim Command for the Dial Push (DOWN/UP) - these will still be executed on a pressed turn
- Fixed rare Condition of Composite Actions being reset when they encountered an Exception during Loading/Creation
- Updated NuGet Packages
- Updated MSFS SimConnect SDK


### Installer
- Set StreamDeck SW 7.5.1 as Target
- Set .NET 10.0.11 as Target