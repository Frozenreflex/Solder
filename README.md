# Solder

A desktop-based script compiler for Resonite

## Installation

### Compiling the Editor

1. Download Godot 4.2 .NET for your target platform, and it's dependencies, such as the .NET 6 SDK

    Note that this is specifically 4.2 .NET, not 4.2.2 or 4.3, or the non-.NET versions. I will likely update to 4.3 later.

   https://godotengine.org/download/archive/4.2-stable/
2. Run the Godot executable to open the Project Manager
3. Click import and browse to the location of the editor project
4. Click on the editor in the Project Manager to open the Godot Editor 
5. Click the hammer icon in the top right to build, and the play button to run the script editor

### Compiling the Mod

1. Ensure you can compile code for .NET Framework 4.7.2. I don't know how to set this part up for Windows and I don't remember how I did it on Linux.
2. Install and ensure ResoniteModLoader is set up for compiling mods (https://github.com/resonite-modding-group/ResoniteModLoader)
3. Run ``dotnet build`` on the mod directory, or use your preferred IDE to build the mod
4. The mod binary can be found in the resulting ``bin/Debug/net4.7.2`` directory, and can be installed like a normal ResoniteModLoader mod

### Notes for Compiling

Both projects are set up with a rudimentary system for linking to Resonite dependencies. If you installed Resonite in a non-standard location, you will need to add your location to both project's .csproj files. the directory paths look something like this.

```xml
<ResonitePath Condition="Exists('/mnt/LocalDisk2/SteamLibrary/steamapps/common/Resonite/')">/mnt/LocalDisk2/SteamLibrary/steamapps/common/Resonite/</ResonitePath>
```

Replace PATH with the path to the root of your Resonite install
```xml
<ResonitePath Condition="Exists('PATH')">PATH</ResonitePath>
```