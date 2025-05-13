<p align="center"> <img src="icon_fullres.png" alt="NetRadio icon" width="200"/> </p> 
<h1> <p align="center" > NetRadio </p> </h1> 

 A Bomb Rush Cyberfunk mod that allows you to listen to online radio stations live in-game.\
 For more information or release downloads, [check the Thunderstore page.](https://thunderstore.io/c/bomb-rush-cyberfunk/p/FunkyUncles/NetRadio/)
## Building from Source
This plugin requires the following .dlls to be placed in the \lib\ folder to be built:
- A [publicized](https://github.com/CabbageCrow/AssemblyPublicizer) version of the game's code, from BRC's Data folder (Assembly-CSharp.dll)
- [CommonAPI.dll by Lazy Duchess](https://github.com/LazyDuchess/BRC-CommonAPI/releases)
- 0Harmony.dll and BepInEx.dll from \BepInEx\core
- Some Unity Engine .dlls from Bomb Rush Cyberfunk's Data folder:
   - UnityEngine.UI.dll
   - Unity.TextMeshPro.dll

With these files, run "dotnet build" in the project's root folder (same directory as NetRadio.csproj) and the .dll will be in the \bin\ folder. To run the build, ensure the files/folders in /include/ are placed in the same location as the .dll file.  
## Credits
Special thanks to:
- Thunder_Kick for putting up with lots and lots of troubleshooting
- RappyTap, TSS, Thunder_Kick, and Wonderstrik for their input on NetRadio's branding and the FunkyUncleFM logo (designed by me!)
- Lazy Duchess for the CommonAPI plugin and example code that made this mod possible
- The developers behind CSCore, the C# audio library NetRadio is built off of

Full credits can be found on [the Thunderstore page.](https://thunderstore.io/c/bomb-rush-cyberfunk/p/FunkyUncles/NetRadio/)
