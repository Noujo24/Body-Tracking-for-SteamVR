# Dependencies

**Nothing is redistributed in this folder.** Every runtime dependency is already present in your Gorilla Tag installation or in BepInEx, or must be installed by you separately.

| Name | Purpose | Compatible version | Obtain from | Install location | Beside the mod DLL? |
|---|---|---|---|---|---|
| BepInEx | Plugin loader, config, logging | 5.4.23.x (x64, Unity Mono) | Already installed | `Gorilla Tag\BepInEx\` | No |
| HarmonyX (`0Harmony.dll`) | Patch for `GorillaIKMgr.CopyInput` | 2.9.0 (ships with BepInEx 5.4.23) | Already installed | `BepInEx\core\` | No |
| `Unity.XR.OpenVR.dll` + `openvr_api.dll` 1.13.10 | OpenVR API bindings and native client | Game build 25815603 | Ships with the game | `Gorilla Tag_Data\Managed\`, `Gorilla Tag_Data\Plugins\x86_64\` | No |
| `Unity.XR.Management.dll`, `UnityEngine.*`, `UnityEngine.UI.dll`, `Unity.InputSystem.dll` 1.14.2, `Newtonsoft.Json.dll` 13.0 | Engine, UI, input, JSON | Game build 25815603 | Ships with the game | `Gorilla Tag_Data\Managed\` | No |
| `Assembly-CSharp.dll` | Game types (`GorillaIK`, `VRRig`, `GorillaTagger`, …) | Game build 25815603 | Ships with the game | `Gorilla Tag_Data\Managed\` | No (proprietary, never copied) |
| SteamVR | Runtime that exposes the trackers | Current Steam release | Steam | Steam library | No |
| OpenVR Space Calibrator (or equivalent) | **Only for non-Lighthouse headsets** (Quest, Pico, WMR): aligns headset and tracker spaces | Any current release | Its project page | Its own installer | No |

Compile-time references point straight at your installed game (see `GTSteamVRBodyTracking/Directory.Build.props`) with `Private=false`, so no game assembly is copied into the build output.
