# GTSteamVRBodyTracking — Game Integration Notes

Technical findings from inspecting Gorilla Tag's own assemblies (metadata and IL), which the mod's design is based on. Section numbers match the original inspection report; sections about the author's local machine are omitted.

## 2. Game version

| Item | Value | Source |
|---|---|---|
| Steam AppID | 1533390 | `steam_appid.txt`, `appmanifest_1533390.acf` |
| Steam build ID | **25815603** | `appmanifest_1533390.acf` (`buildid` = `TargetBuildID`) |
| Last updated | 1791913498 (Unix) ≈ 2026-10-13 | `appmanifest_1533390.acf` |
| Unity version | **6000.2.9f1** (`e0c4e791ab71`) | `Gorilla Tag.exe` / `UnityPlayer.dll` version resources, confirmed by BepInEx log |
| Build GUID | `16e9a145782747bba8bd8858b0e58441` | `Gorilla Tag_Data\boot.config` |
| Company / product | Another Axiom / Gorilla Tag | `Gorilla Tag_Data\app.info` |

The install has no human-readable game version string. `StreamingAssets\build_info` contains a stale 2020 timestamp and is not meaningful. **The Steam build ID 25815603 is the version identifier used in this project.**

## 3. Runtime and mod loader

| Item | Value |
|---|---|
| Scripting backend | **Mono** (`MonoBleedingEdge\`, `Gorilla Tag_Data\Managed\*.dll`; not IL2CPP) |
| CLR | 4.0.30319.42000; reflection emit supported (BepInEx log) |
| Class library | Unity Mono profile with `netstandard.dll` 2.1 facade. Game assemblies reference `netstandard 2.1.0.0`. |
| Mod loader | **BepInEx 5.4.23.5** (BepInEx log header, `BepInEx.dll` file version) |
| Doorstop | 4.5.0 (`.doorstop_version`, `winhttp.dll`, `doorstop_config.ini`) |
| Harmony | `BepInEx\core\0Harmony.dll` **2.9.0.0** (HarmonyX), plus `0Harmony20.dll` and `HarmonyXInterop.dll` |
| Chainloader | `HideManagerGameObject = true` (`BepInEx.cfg`) |
| Logging | Disk log `BepInEx\LogOutput.log`; Unity log listening on; Harmony logger Warn/Error only |
| Config convention | `BepInEx\config\<plugin GUID>.cfg` through `BepInEx.Configuration.ConfigFile`. Some mods also keep JSON beside it (for example GorillaShirts). |

## 4. XR / SteamVR stack

| Item | Value | Source |
|---|---|---|
| XR boot loader | `xrsdk-pre-init-library=XRSDKOpenVR` | `boot.config` |
| XR plugins present | XRSDKOpenVR 1.0.0-preview.1 (display + input), OpenXR 1.15.1 | `UnitySubsystems\*\UnitySubsystemsManifest.json` |
| OpenVR settings | `InitializationType: 1` (Scene), action manifest `StreamingAssets\SteamVR\actions.json` | `StreamingAssets\SteamVR\OpenVRSettings.asset` |
| Native OpenVR | `Plugins\x86_64\openvr_api.dll` **1.13.10** | file version |
| Managed OpenVR bindings | **`Unity.XR.OpenVR.dll`** contains `Valve.VR.OpenVR`, `CVRSystem`, `CVRCompositor`, `TrackedDevicePose_t`, … (interface `IVRSystem_022`) | IL inspection |
| SteamVR Unity plugin | `SteamVR.dll` (`Valve.VR.SteamVR`, `SteamVR_Behaviour`, …). It does **not** contain the OpenVR interop. | IL inspection |

**Game use of OpenVR:** in `Assembly-CSharp` only `KIDUI_InputFieldController` touches `Valve.VR` (the overlay keyboard). The game never calls `PollNextEvent` from managed code, but the mod still must not consume events (see section 9).

### 4.1 OpenVR API verified for use (all in `Unity.XR.OpenVR.dll`)

| Member | Verified signature / behaviour |
|---|---|
| `OpenVR.System` | Getter → `COpenVRContext.VRSystem()`. It calls `CheckClear()` (compares `VRToken` with `GetInitToken()`), then lazily calls `OpenVRInterop.GetGenericInterface("FnTable:IVRSystem_022")`. **It attaches to the OpenVR session the XR loader already opened in this process, without calling `OpenVR.Init`.** |
| `OpenVR.Compositor` | Same lazy pattern. `GetLastPoses(TrackedDevicePose_t[] render, TrackedDevicePose_t[] game) → EVRCompositorError` and `GetTrackingSpace()` exist. |
| `OpenVR.Init(ref EVRInitError, EVRApplicationType, string)` / `Shutdown()` | Exist. `EVRApplicationType.VRApplication_Background = 3`. |
| `OpenVR.IsRuntimeInstalled()`, `IsHmdPresent()`, `k_unMaxTrackedDeviceCount` | Exist. |
| `CVRSystem.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin, float, TrackedDevicePose_t[])` | Exists. |
| `CVRSystem.GetTrackedDeviceClass(uint)` | `ETrackedDeviceClass`: Invalid=0, HMD=1, Controller=2, **GenericTracker=3**, TrackingReference=4, DisplayRedirect=5. |
| `CVRSystem.IsTrackedDeviceConnected(uint)` | Exists. |
| `CVRSystem.GetStringTrackedDeviceProperty(uint, ETrackedDeviceProperty, StringBuilder, uint, ref ETrackedPropertyError)` | Exists. |
| `CVRSystem.GetFloatTrackedDeviceProperty`, `GetBoolTrackedDeviceProperty` | Exist. |
| `ETrackedDeviceProperty` | `Prop_TrackingSystemName_String=1000`, `Prop_ModelNumber_String=1001`, **`Prop_SerialNumber_String=1002`**, `Prop_RenderModelName_String=1003`, `Prop_ManufacturerName_String=1005`, `Prop_DeviceBatteryPercentage_Float=1012`, `Prop_DeviceProvidesBatteryStatus_Bool=1026`, `Prop_ControllerType_String=7000`. |
| `TrackedDevicePose_t` | `mDeviceToAbsoluteTracking` (`HmdMatrix34_t` m0..m11), `vVelocity`, `vAngularVelocity`, `eTrackingResult`, `bPoseIsValid`, `bDeviceIsConnected`. |
| `ETrackingResult` | Uninitialized=1, Calibrating_InProgress=100, Calibrating_OutOfRange=101, **Running_OK=200**, Running_OutOfRange=201, Fallback_RotationOnly=300. |
| `ETrackingUniverseOrigin` | Seated=0, Standing=1, RawAndUncalibrated=2. |
| XR loader query | `UnityEngine.XR.Management.XRGeneralSettings` → `XRManagerSettings.activeLoader`. `Unity.XR.OpenVR.OpenVRLoader` exists. |

## 5. Local player and character rig (Assembly-CSharp)

### 5.1 Access points (verified public members)

| Type | Member | Notes |
|---|---|---|
| `GorillaTagger` | `static Instance`, `offlineVRRig : VRRig`, `mainCamera : GameObject`, `leftHandTriggerCollider` / `rightHandTriggerCollider : GameObject`, `leftHandTransform` / `rightHandTransform`, `headCollider`, `bodyCollider` | The hand trigger objects are the game's own button-press fingertips. |
| `GorillaLocomotion.GTPlayer` | `static Instance`, `mainCamera : Camera`, `turnParent : GameObject`, `scale` (get), `headCollider`, `bodyCollider` | Locomotion and physics owner. **The mod does not write to it.** |
| `VRRig` | `static LocalRig` (get), `isOfflineVRRig`, `head`/`leftHand`/`rightHand : VRMap`, `bodyTransform`, `mainSkin`, `scaleFactor` | `myIk` is private; the game itself gets it with `GetComponent<GorillaIK>()`. |
| `VRMap` | `rigTarget`, `overrideTarget`, `trackingPositionOffset`, `trackingRotationOffset`, `MapMine(float, Transform)` | Head and hands only. **No body, leg or foot maps.** |
| `ControllerInputPoller` | `static instance`, `left/rightControllerPrimaryButton`, `left/rightControllerSecondaryButton`, `…GripFloat`, `…IndexFloat`, `…TriggerButton` | Usable for the VR menu-open gesture. |

### 5.2 The actual rig skeleton (`GorillaIK`, attached to each `VRRig`)

`GorillaIK` exposes exactly these bones: `headBone`, `bodyBone`, `leftUpperArm`, `leftLowerArm`, `leftHand`, `rightUpperArm`, `rightLowerArm`, `rightHand`. Targets: `targetHead`, `targetLeft`, `targetRight`.

**There are no hip, knee, leg or foot bones.**

IK pipeline, from IL:

1. `GorillaIKMgr.LateUpdate()` → `playerIK?.SkeletonUpdate()` → `CopyInput()` (private) → `IKJob` (completed immediately) → `CopyOutput()` → schedules `IKTransformJob`.
2. `CopyInput()` reads, per `GorillaIK`, `usingUpdatedIK`, `leftElbowDirection`, `rightElbowDirection`, and `targetBodyRot`. `targetBodyRot` is used only if `usingUpdatedIK`; otherwise `bodyInitialRot` is used. It also calls `GetShoulderLocalTargetPos_*`, which uses `projectedLeft/RightShoulderPosition` when `projectedBodyRotation != null && usingUpdatedIK`, else `leftUpperArm.parent`.
3. In `IKJob.Execute`, when `usingNewIK`, the upper arm is twisted about the shoulder→hand axis so that the elbow lies toward `elbowDir`. `elbowDir` is a pole direction in shoulder-local space.
4. `IKTransformJob`: indices 0–4 (upper/lower arms, **`bodyBone`**) are written as `localRotation`. Head and hands use world `rotation`; hands also get `localPosition`. So `lerpBodyRot` is `bodyBone.localRotation`.

**The game's own body-tracking path.** `GorillaIK.SkeletonUpdate()` is Meta/Quest body tracking, gated by an Android permission and `SubscriptionManager`. It sets:
- `usingUpdatedIK = true`
- `targetBodyRot = Inverse(bodyBone.parent.rotation) * worldBodyRot * bodyOffsetRotation (* leanOffsetRotation)`
- `projectedBodyRotation.localRotation = targetBodyRot`
- `leftElbowDirection = projectedLeftShoulderPosition.InverseTransformDirection(((elbowPos - up*biasDistance) - targetLeft.position).normalized)`, and likewise for the right side.

On PC, `GorillaIKMgr.playerIK` is assigned only in `PermissionGranted` (Android). **This path is idle on PC**, but the fields and the IK job that consumes them are active for every rig.

### 5.3 Side effects found (these drive the design)

| Reader | What it does with the IK fields | Consequence |
|---|---|---|
| `VRRig.SerializeWriteShared` | Sends `usingNewIK = ShouldUseNewIKMethod(myIk.usingUpdatedIK)`, and if true also `targetBodyRot`, `rightElbowDirection`, `leftElbowDirection`, **to the network** | If the mod left these fields set, other players could receive the pose. **Forbidden for v1 (Section 13).** |
| `VRRig.ShouldUseNewIKMethod` | For the local rig: `SubscriptionManager` feature **and** `myIk.usingUpdatedIK` | |
| `GTPlayer.get_bodyInitialHeight` (used by `GTPlayer.BodyCollider()` and `LateUpdate`) | If `GorillaIK.playerIK.usingUpdatedIK`, the **body collider height** becomes `max(0.2, dot(bodyBone.up, up)) * _bodyInitialHeight` | Leaving `usingUpdatedIK = true` could change **gameplay physics**. **Forbidden.** |
| `GorillaIK.OnDisable` / `ResetIKData()` | Resets elbow directions to zero, `targetBodyRot`/`lerpBodyRot` to `bodyInitialRot`, `usingUpdatedIK = false` | Gives a known-safe restore state. |

### 5.4 Resulting integration plan (verified feasible)

A Harmony **Prefix + Finalizer on `GorillaIKMgr.CopyInput`** (private; patched by name, no publicizer needed):
- **Prefix:** for the local `GorillaIK` only, and only when a valid, fresh tracker pose exists, save `usingUpdatedIK`, `targetBodyRot`, `left/rightElbowDirection` and `projectedBodyRotation.localRotation`. Then write tracker-derived values using the same formulas as `SkeletonUpdate`.
- **Finalizer** (runs even if `CopyInput` throws): restore every saved value.

The IK job consumes the overrides inside `CopyInput`, so the bones are posed visually. `SerializeWriteShared` and `GTPlayer` run outside that window and always see the original values. That means no network change and no collider change. When a tracker is invalid or the mod is disabled, nothing is overridden, and the next `CopyInput` resets `lerpBodyRot` to `bodyInitialRot` (the non-player branch copies targets without lerp). No persistent change is made to any game object.

### 5.5 Body-role support on the real rig

| Mod role | Rig target | Status |
|---|---|---|
| Waist/pelvis | `bodyBone` rotation (fallback when there is no chest tracker) | **Supported** (rotation only) |
| Chest | `bodyBone` rotation (preferred) | **Supported** (rotation only) |
| Left / right elbow | `leftElbowDirection` / `rightElbowDirection` (arm twist / elbow pole) | **Supported** (direction only; hands still follow controllers) |
| Left / right foot | — | **Not supported:** the rig has no leg or foot bones. Debug marker only. |
| Left / right knee | — | **Not supported** (no legs). Marker only. |
| Left / right hip | — | **Not supported** (one torso bone, no pelvis or legs). Marker only. |
| Custom | — | Marker only. |

Body *position* is not applied. The torso is attached to the head-driven rig, and moving it would desync visuals from colliders.

## 6. UI and input

| Item | Finding |
|---|---|
| Existing menu frameworks | **None installed.** Plugins: Utilla 1.7.2, Bepinject, Extenject, GorillaShirts, GorillaCraft, others; no ComputerInterface or menu library. Reference mods were not provided. |
| Chosen UI | Unity **world-space `Canvas`** (`UnityEngine.UI.dll` from the game), legacy `Text` with the built-in `LegacyRuntime.ttf` (with a fallback). |
| VR interaction | Custom **fingertip poke**: math only, at the positions of `GorillaTagger.left/rightHandTriggerCollider`, the same fingertips the game uses for its own buttons. **No colliders or physics layers are added**, so arm locomotion is untouched. Desktop mouse ray as a secondary path. |
| Keyboard | Unity Input System **1.14.2** (`Unity.InputSystem.dll`; Assembly-CSharp references it; installed mods TimeTab and Bark use `Keyboard`). F8 through `Keyboard.current`, with a guarded fallback to legacy `Input`. |
| VR open/close | Configurable gesture read from `ControllerInputPoller.instance` (default: hold both secondary buttons). On-menu Close button. |

## 8. Dependencies

Every runtime dependency already ships with the game or BepInEx. **Nothing is bundled or redistributed.**

| Dependency | Version | Provided by | Purpose |
|---|---|---|---|
| BepInEx (`BepInEx.dll`) | 5.4.23.5 | Installed loader | Plugin base, config, logging, paths |
| HarmonyX (`0Harmony.dll`) | 2.9.0.0 | Installed loader | `GorillaIKMgr.CopyInput` prefix/finalizer |
| `Unity.XR.OpenVR.dll` | game build | Game | OpenVR managed interop (`Valve.VR.*`) |
| `openvr_api.dll` (native) | 1.13.10 | Game | OpenVR runtime client |
| `Unity.XR.Management.dll` | game build | Game | Detecting the active XR loader |
| `Assembly-CSharp.dll` | build 25815603 | Game | `GorillaTagger`, `VRRig`, `GorillaIK`, `GorillaIKMgr`, `GTPlayer`, `ControllerInputPoller` |
| `UnityEngine.*Module.dll`, `UnityEngine.UI.dll` | Unity 6000.2.9f1 | Game | Engine, UI |
| `Unity.InputSystem.dll` | 1.14.2 | Game | F8 key |
| `Newtonsoft.Json.dll` | 13.0.0.0 | Game | Tracker JSON config |
| SteamVR (external app) | user-installed | Valve / Steam | Runtime that exposes the trackers |

Compile-time references point to the files in your install. They are not copied to the output (`<Private>false</Private>`).

## 9. Design constraints derived from inspection

1. **Do not call `OpenVR.Init` when the game's OpenVR XR loader is active.** Attach through `OpenVR.System`. Standalone `VRApplication_Background` init is a guarded fallback only when no OpenVR session exists, and only then does the mod call `OpenVR.Shutdown()`.
2. **Never call `PollNextEvent`.** It consumes the shared event queue. Connect/disconnect is detected by polling `IsTrackedDeviceConnected`, device class and serial on a timer, plus `bDeviceIsConnected` per frame.
3. **Identity** = `Prop_SerialNumber_String`, never the device index. A device with no readable serial is "unidentifiable": usable this session, never auto-restored from config.
4. **Only `ETrackedDeviceClass.GenericTracker`** is treated as a body tracker. HMDs, controllers and base stations are excluded by class.
5. **Valid pose** = connected, `bPoseIsValid`, and `eTrackingResult == Running_OK`. Anything else is shown as invalid and never applied.
6. **Coordinates:** OpenVR is right-handed (+Y up, −Z forward). Unity is left-handed. The conversion mirrors Z: position `(m3, m7, −m11)`, rotation `S·R·S` with `S = diag(1,1,−1)`. Tracking space → world is resolved **at runtime** from the HMD:
   - **Tracking-origin mode:** if `mainCamera.localPose` matches the converted OpenVR HMD pose, map through `mainCamera.parent`.
   - **HMD-anchor mode** otherwise: `world = cameraWorld · inverse(hmdTracking) · tracker`, scaled by `camera.parent.lossyScale`.
   - The mode and the measured error are shown in the UI.
7. **Smoothing happens in tracking space** (before world mapping), so locomotion never lags the markers.
8. **Avatar writes are scoped to `CopyInput`** (section 5.4) to keep them local-only and physics-free.
9. Debug primitives have their `Collider` destroyed. UI uses no colliders. All objects live under one `DontDestroyOnLoad` root that is destroyed on unload.

