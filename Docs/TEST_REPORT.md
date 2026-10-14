# GTSteamVRBodyTracking — Test Report (v0.1.0)

Test date: 2026-10-13.

> **Update:** after this report was written, the author ran the mod in Gorilla Tag with real Vive Trackers. Tracker detection, the menu and torso tracking were reported working. The detailed in-game checklist below has not been formally completed.

This report keeps three kinds of evidence apart:

- **Build checks:** compilation and static verification of the DLL against your installed game.
- **Automated tests:** 40 tests / 1,836 assertions on the Unity-independent logic, run on .NET 8.
- **In-game hardware tests:** **NOT performed.** I did not run Gorilla Tag, SteamVR or a physical Vive Tracker. These are listed as a procedure for you to run.

---

## 1. Build checks (performed)

| Check | Result |
|---|---|
| `dotnet build GTSteamVRBodyTracking.sln -c Release` | **Succeeded: 0 warnings, 0 errors** |
| Artifact | `GTSteamVRBodyTracking.dll`, 151,040 bytes, assembly `GTSteamVRBodyTracking, Version=0.1.0.0`, target `.NETStandard 2.1` |
| Plugin entry point | `GTSteamVRBodyTracking.Plugin : BepInEx.BaseUnityPlugin` with `[BepInPlugin("adrian.gorillatag.steamvrbodytracking", "GTSteamVRBodyTracking", "0.1.0")]` |
| Assembly references | netstandard 2.1, BepInEx 5.4.23.5, 0Harmony 2.9.0.0, Assembly-CSharp, Unity.XR.OpenVR, Unity.XR.Management, Unity.InputSystem 1.14.2, Newtonsoft.Json 13.0.0.0, UnityEngine.CoreModule / UI / UIModule / TextRenderingModule / PhysicsModule / InputLegacyModule. All are provided by the game or BepInEx. |
| **Runtime resolution** (`Tools/GTInspect resolve`) | Every type and member reference in the DLL was resolved **only** against `Gorilla Tag_Data\Managed` and `BepInEx\core`: **675 resolved, 0 failed.** No API is missing from the game's real (possibly stripped) Mono libraries. |
| Game members used | Only the members verified in the game inspection (`GorillaIK` fields, `GorillaIKMgr.playerIK`, `VRRig.LocalRig`, `GorillaTagger` hand triggers/camera/haptics, `GTPlayer.Instance/mainCamera`, `ControllerInputPoller` buttons). **No writes** to `GTPlayer`, colliders, rigidbodies or networking. |
| Bundled files | DLL only. No game or third-party assemblies are copied (`Private=false`). |

## 2. Automated tests (performed, all passing)

Run with `dotnet run --project GTSteamVRBodyTracking.Tests -c Release`. Output: **40/40 tests passed (1836 assertions).**

| Area | Tests |
|---|---|
| Math (Unity conventions) | EulerMatchesUnityConvention, QuaternionAlgebra, MatrixToQuaternionRoundTrip, YawExtraction, RigidPoseInverse, AnchorMapsHeadsetExactlyAndPreservesRelativeLayout |
| OpenVR → Unity conversion | IdentityAndTranslationFlipZ, PhysicalLeftTurnStaysALeftTurn, PitchUpStaysPitchUp, RoundTripRandomPoses (300 poses), RejectsUnusableMatrices, TrackerInFrontOfHeadsetEndsUpInFrontOfCamera, PoseValidityRules |
| Discovery / identity | NoTrackersAndNonTrackersIgnored, OneAndMultipleTrackers (6 trackers, no duplicates on rescan), DisconnectAndReconnectOnDifferentIndex, IndexReuseByAnotherDeviceDoesNotInheritIdentity, UnreliableSerialIsSessionOnly, DuplicateSerialsAreFlaggedNotTrusted, SavedPlaceholderBindsWhenDeviceAppears, ForgetOnlyDisconnected, SessionLossMarksEverythingDisconnected |
| Roles | ConflictsOnlyForUniqueEnabledRoles, RoleParsingAndSupport, AssignReassignUnassign (move-here, swap, conflict, keep-only, Custom sharing) |
| Persistence | SaveLoadRoundTrip, InvalidValuesAreRepairedOnLoad, CorruptFileRecoversFromBackupAndInterruptedWriteIsHarmless, RestoreDefaultsIsInMemoryUntilSaved, PerTrackerSettingsAreIndependent |
| Filtering / calibration / avatar math | ZeroSmoothingIsPassThrough, SmoothingConvergesAndIsFrameRateIndependent, SnapsAfterGapOrReset, OffsetsAreInTrackerLocalFrame, CalibrationMountYieldsHeadsetYawAndFollowsTracker (incl. no accumulation), RecenterFixesYawOnly, CaptureStabilityCheck, HeadRelativePositionUsesYawFrame, ClampLimitsAngle, BodyBoneRotationNeutralAndTurned |

What the automated tests do **not** prove: real OpenVR behaviour, Unity rendering and interaction, the Harmony patch in the running game, and whether the camera and rig behave as inspected. Those need the in-game tests below.

## 3. Checklist status (Section 12)

Legend: **B** = build/static check, **A** = automated test, **G** = in-game/hardware test (not yet performed).

| # | Item | Status |
|---|---|---|
| 1 | Plugin startup and shutdown | B: entry point and cleanup paths compile; resolution OK. **G pending** |
| 2 | SteamVR unavailable | A: session loss → all disconnected. Code catches `DllNotFound`, `EntryPointNotFound` and init errors and retries. **G pending** |
| 3 | No trackers connected | A (registry). **G pending** |
| 4 | One tracker | A. **G pending** |
| 5 | Multiple trackers | A (6). **G pending** |
| 6 | Disconnect / reconnect | A (index change, index reuse). **G pending** |
| 7 | Dynamic tracker list | A (no duplicates, removal of session-only). UI binding by key. **G pending** |
| 8 | Role assignment / reassignment | A. **G pending** |
| 9 | Duplicate role warnings | A (detector). Dashboard/card/popup warnings. **G pending** |
| 10 | Position and rotation offsets | A (tracker-local frame, limits, independence). **G pending** |
| 11 | Per-tracker smoothing | A. **G pending** |
| 12 | Calibration | A (math, stability rejection, no accumulation). **G pending** |
| 13 | Coordinate conversion | A (handedness, round trip, anchor). Runtime mode verification is built in. **G pending** |
| 14 | Debug-marker movement | **G pending** |
| 15 | Identify button | **G pending** |
| 16 | UI open/close | **G pending** |
| 17 | VR menu interaction | **G pending** |
| 18 | Save / reload | A (round trip, atomic write, backup). **G pending** |
| 19 | Missing or changed tracker IDs | A (placeholders, unreliable/duplicate serials). **G pending** |
| 20 | Disabling the mod | Code path: no override, markers hidden, patch removed on unload. **G pending** |
| 21 | Safe avatar behaviour after tracking loss | A (filter reset), plus the code rule that only poses valid **this frame** are used. **G pending** |
| 22 | Compilation into the DLL | **B: done** |

## 4. In-game test procedure (for you to run)

Install as described in README §3. Set `Debug → Verbose Logging = true` for the first session, and keep `BepInEx\LogOutput.log` open.

1. **Startup.** The log shows `Avatar patch installed on GorillaIKMgr.CopyInput.`, `Ready.`, then `Attached to the game's OpenVR session.`, and later `Local rig diagnostics: …`. Please send me that diagnostics line. It confirms the prefab references the integration relies on.
2. **SteamVR unavailable.** Start the game without SteamVR (if your setup allows it). The dashboard says *SteamVR: Waiting…* and there are no errors flooding the log.
3. **No trackers.** The dashboard warns *No Vive Trackers detected*. The tracker list shows the empty-state text.
4. **One tracker, then 5. multiple.** Cards appear within about 1 s, with the correct serials. Headset and controllers are not listed.
5. **Disconnect/reconnect.** Turn one tracker off: within about 1 s its card says *Disconnected*, its marker turns red, and the role is kept. Turn it on again: the same card reconnects, with no duplicate.
6. **Assign roles.** Assign Chest to tracker A, then pick Chest for tracker B and test **Move here**, **Swap** and **Assign anyway**. The conflict warning appears and the Resolve page fixes it.
7. **Offsets and smoothing.** Change offsets on tracker A only. Only A's marker moves. Raise smoothing and confirm the marker lags slightly but never lags your locomotion.
8. **Coordinate check.** Dashboard → *Coordinates* should read *Tracking origin (verified)* with an error under about 1 cm. *Headset anchor* also works. Move each tracker forward, left and up, and rotate it: its marker must match **direction and rotation**.
9. **Identify.** Each card's Identify flashes the right marker.
10. **Calibrate.** Follow README §6. Try moving during capture: it must fail with a reason. After success and saving, the yellow body arrows point forward.
11. **Avatar.** With Chest or Waist calibrated, twist your torso: the gorilla torso turns (watch in a mirror). Elbow trackers change the arm twist. Cover a tracker: the torso snaps back to normal. Ask a friend in a private room to confirm **they see nothing unusual**.
12. **Physics sanity.** Movement, jumping and tagging feel identical with the mod on and off.
13. **Persistence.** Save, restart the game: assignments, offsets, smoothing and calibration return. Then test **Restore defaults**, **Reload config from disk**, and a deliberately corrupted JSON (it should recover from `.bak`).
14. **Disable.** Dashboard → Mod: OFF. Markers disappear and the avatar returns to normal, while tracker status keeps updating.

Please report any failure with the log excerpt and the dashboard's *Coordinates* line.
