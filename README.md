# Body Tracking for SteamVR (Gorilla Tag)

Brings Gorilla Tag's body tracking to the **Steam (PC VR)** version using SteamVR trackers. Your gorilla's torso turns with a chest or waist tracker, and its elbows follow elbow trackers.

<img width="1920" height="1080" alt="Gorilla Tag body tracking with SteamVR trackers" src="https://github.com/user-attachments/assets/f4da796d-3c02-474d-a4b2-33bf49358cde" />

> [!WARNING]
> This is an unofficial mod. Use it in **modded lobbies only** and at your own risk. It is not affiliated with Another Axiom.

**Who sees it?** Only you. The tracking moves your local gorilla and is never sent to other players. It also doesn't change your hitbox or movement physics.

**Built for:** Gorilla Tag on Steam, BepInEx 5.4.23, mod version 0.1.1. A game update can break the mod. If it stops working after an update, open an issue.

## Requirements

- Gorilla Tag on Steam (PC VR)
- [BepInEx 5.4.23](https://github.com/BepInEx/BepInEx/releases) (x64, Unity Mono)
- SteamVR
- Body trackers paired in SteamVR. The mod is built for **HTC Vive Trackers** with base stations. Other trackers that SteamVR lists as trackers (for example Tundra or SlimeVR) should also be detected, but they're untested.
- **Quest / Pico / WMR headsets only:** [OpenVR Space Calibrator](https://github.com/pushrax/OpenVR-SpaceCalibrator) (or similar), to line up your headset with your trackers

No Vive trackers? [This video](https://www.youtube.com/watch?v=BDgjwTNfF60&t=1s) shows an alternative setup.

## Installation

1. Install BepInEx 5 into your Gorilla Tag folder and launch the game once, so the `BepInEx\plugins` folder gets created.
2. Download `GTSteamVRBodyTracking.dll` from the [Releases](../../releases) page.
3. Close Gorilla Tag.
4. Put the DLL in:
   ```
   Gorilla Tag\BepInEx\plugins\
   ```
   (Steam default: `C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag\BepInEx\plugins\`)
5. Start **SteamVR**, turn on your trackers, and make sure SteamVR shows them as tracked (solid icons).
6. Start Gorilla Tag.

To check it loaded, open `BepInEx\LogOutput.log` and look for a line containing `GTSteamVRBodyTracking` and `Ready.`

## Setup

1. **Open the menu:** hold **B + Y** in VR, or press **F8** on the keyboard.
2. **Find your trackers:** press **Configure trackers**. Each tracker has its own card. Press **Identify** to make its marker flash yellow, then move a physical tracker to see which one it is.
3. **Assign body parts:** press **Role** on each card and choose:
   - **Chest** or **Waist/pelvis**: turns your gorilla's torso. Chest is used if you have both.
   - **Left elbow** / **Right elbow**: controls your elbow direction.
   - Feet, knees and hips show a marker only, because the gorilla has no legs.
4. **Check the markers:** move and rotate each tracker. Its marker should follow it exactly. If markers are shifted or rotated by a fixed amount, run OpenVR Space Calibrator first.
5. **Calibrate** (needed for the torso):
   1. Go back to the dashboard and press **Calibrate**.
   2. Stand straight, arms relaxed at your sides, face forward and look straight ahead.
   3. Press **Start calibration**. You get a 3-second countdown, then hold still for 1 second.
   4. Press **Save calibration**.
6. **Save:** press **Save configuration** on the dashboard, so everything loads automatically next time.

The **Avatar** line on the dashboard should now say something like `Active: torso, left elbow`. Look in a mirror to check.

**Optional:** switch **Tracker markers** to OFF on the dashboard to hide the markers. Tracking keeps working.

## Controls

| Action | How |
|---|---|
| Open / close the menu (VR) | Hold **B + Y** (top face buttons on both controllers) for about half a second |
| Open / close the menu (keyboard) | **F8** |
| Press a menu button | **Touch it with your fingertip** (like the game's own buttons). A dot shows where your finger is, and your controller vibrates on press. |
| Press the next button | Pull your finger back a few centimetres first |
| Move a slider | Touch the slider and slide your finger along it |
| Click with the mouse | Left-click on the menu (desktop alternative) |
| Close the menu | **Close X** (top right), or the same gesture or key |
| Bring the menu back in front of you | **Move menu here** on the dashboard |
| Go back a page | **< Back** (top left) |

You can change the keyboard key and the VR gesture in `BepInEx\config\noujo24.gorillatag.steamvrbodytracking.cfg` (edit with the game closed):
- `Toggle Key`: for example `F8`, `F9`, `Backquote`
- `VR Gesture`: `BothSecondaryButtons`, `LeftSecondaryButton`, `RightSecondaryButton`, `LeftPrimaryAndSecondary`, or `Disabled`
- `VR Gesture Hold Seconds`: how long to hold the gesture

## Troubleshooting

| Problem | Try this |
|---|---|
| No `Ready.` line in `LogOutput.log` | Make sure BepInEx is installed (a `BepInEx` folder with `core`, `config` and `plugins` inside) and the DLL is in `BepInEx\plugins`. Check the log for errors mentioning `GTSteamVRBodyTracking`. |
| The menu doesn't open | Hold **B + Y** for a full half second, or press **F8** with the game window focused. Check the `VR Gesture` and `Toggle Key` settings in the config file. |
| "No Vive Trackers detected" | Turn the trackers on and wait until SteamVR shows them as solid icons, then press **Refresh**. Headsets, controllers and base stations are not listed. |
| Markers are shifted or rotated | Your headset and trackers aren't lined up. Run OpenVR Space Calibrator, then check the markers again. |
| Torso doesn't move | Assign a **Chest** or **Waist/pelvis** role, run **Calibrate**, then **Save configuration**. |
| Settings reset after updating from 0.1.0 | Version 0.1.1 uses new config file names. Set up your trackers again, or rename your old `...steamvrbodytracking.cfg` and `...trackers.json` files in `BepInEx\config` to start with `noujo24.` |

## Uninstall

Delete `GTSteamVRBodyTracking.dll` from `BepInEx\plugins`. To remove your settings too, delete the `noujo24.gorillatag.steamvrbodytracking` files in `BepInEx\config`.

## Building from source

You need the .NET SDK and Gorilla Tag with BepInEx installed (the project references the game's DLLs).

```
dotnet build GTSteamVRBodyTracking.sln -c Release
```

If your game isn't in the default Steam folder, create `GTSteamVRBodyTracking\GamePath.user.props` (it's git-ignored):

```xml
<Project><PropertyGroup><GamePath>D:\SteamLibrary\steamapps\common\Gorilla Tag</GamePath></PropertyGroup></Project>
```

Add `-p:CopyToPlugins=true` to copy the built DLL into your game's `BepInEx\plugins` folder automatically.

## Credits

- [BepInEx](https://github.com/BepInEx/BepInEx) and [Harmony](https://github.com/pardeike/Harmony) for modding
- [OpenVR Space Calibrator](https://github.com/pushrax/OpenVR-SpaceCalibrator) for lining up mixed tracking systems
- Valve's OpenVR / SteamVR

## License

[MIT](LICENSE)
