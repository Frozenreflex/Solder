# Solder

A desktop-based script compiler for Resonite

## Installation

### Compiling the Editor

The editor requires compiling, and built binaries are not provided, as it uses Resonite as a dependency
1. Download .NET SDK 8 for your target platform. You can find the download link here: https://dotnet.microsoft.com/download/dotnet/8.0
2. Download Godot 4.2 .NET for your target platform. 
    - Note that this is specifically Godot 4.2 .NET, newer versions are unsupported as of this time
    - [Download From Github (Windows)](https://github.com/godotengine/godot/releases/download/4.2-stable/Godot_v4.2-stable_mono_win64.zip)
    - [Download From Github (Linux)](https://github.com/godotengine/godot/releases/download/4.2-stable/Godot_v4.2-stable_mono_linux_x86_64.zip)

3. Run the Godot executable to open the Project Manager.
4. Click import and browse to the location of the editor project.
5. Click on the editor in the Project Manager to open the Godot Editor.
6. Click the hammer icon in the top right to build, and the play button to run the script editor.

### Installing the Mod

Built binaries for the mod are provided in the Releases tab, or you can manually compile it.

### Compiling the Mod

1. Install and ensure ResoniteModLoader is set up for compiling mods. You can find the ResoniteModLoader repository [here](https://github.com/resonite-modding-group/ResoniteModLoader).
2. Run either
    - `make` which will put the mod in `Solder.Client/build`
    - `dotnet build` which will put the mod in `bin/Debug/net4.7.2/Solder.Client.dll`
    - or build with your prefered IDE
3. You can install the mod like any other mod

### Notes for Compiling

Both projects are set up with a rudimentary system for linking to Resonite dependencies. If you installed Resonite in a non-standard location, you will need to add your location to both project's .csproj files. the directory paths look something like this.

```xml
<ResonitePath Condition="Exists('/mnt/LocalDisk2/SteamLibrary/steamapps/common/Resonite/')">/mnt/LocalDisk2/SteamLibrary/steamapps/common/Resonite/</ResonitePath>
```

Replace PATH with the path to the root of your Resonite install
```xml
<ResonitePath Condition="Exists('PATH')">PATH</ResonitePath>
```
