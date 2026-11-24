# Android XR foveation repro

A minimal Unity project that shows **foveated rendering can't be turned on with Android XR**. The app asks OpenXR for the highest fixed foveation level. A floating HUD shows what was requested next to what OpenXR reports back. A ring of high-detail panels makes peripheral blur easy to see.

**Summary:** Unity and the OpenXR runtime both report foveation as active at the maximum level. Even so, no foveation is visible on the device, and the system's XR Performance Data Overlay reports the foveation level as **None**.

## Project baseline

| | |
|---|---|
| Unity | **6000.6.8f1** |
| Template | **Mixed Reality Template** (Unity Hub → New project → Mixed Reality) |
| Render pipeline | URP 17.6.0 |
| Target | Android, Vulkan, IL2CPP, ARM64, min/target SDK 34 |
| Test device | Android XR (`model: Android_XR_Puck`, build `UXP4.260727.001.A3`, userdebug) |

## Steps taken from the template

### 1. Android XR / OpenXR setup

**Packages** (`Packages/manifest.json`), changed from the template defaults:

| Package | Template | Now |
|---|---|---|
| `com.unity.xr.androidxr-openxr` | 1.4.0 | **1.5.1** |
| `com.unity.xr.openxr` | 1.18.0 | **1.19.0** |
| `com.unity.xr.arfoundation` | 6.6.2 | **6.7.1** |
| `com.unity.xr.meta-openxr` | 2.6.1 | **removed** (Quest only) |

**XR Plug-in Management → Android:** OpenXR loader, initialise on startup (template default).

**OpenXR → Android features enabled:** Android XR Support, Android XR build profile, Android XR performance metrics, AR Session / Camera / Plane / Anchor / Bounding Box / Mesh, Display Utilities, Automatic Viewport Dynamic Resolution, Hand Tracking, Hand Interaction Profile, Meta Hand Tracking Aim, Android Mouse Interaction Profile, Composition Layers, Lifecycle, and **Foveated Rendering**.

**Disabled** (template defaults that are Quest-only or not needed): Meta Quest Support, Meta Quest Touch Plus / Pro and Oculus Touch controller profiles, AR Occlusion, AR Raycast, Boundary Visibility.

**Render mode:** Single Pass Instanced / Multi-view.

**Player settings:** application id `com.example.foveationrepro`, landscape left, Vulkan pre-transform on.

OpenXR Project Validation for Android reports **0 issues**.

### 2. Fixed foveation at maximum

- **Foveated Rendering feature:** enabled for Android.
  - Foveated Rendering Method: **Foveated rendering (SRP API)** (`foveatedRenderingApi = SRPFoveation`)
  - **Use eye tracking: off.** This is fixed foveation, so `XR_META_foveation_eye_tracked` is not requested and no eye-tracking permission is needed.
  - Dynamic foveation: off
  - Subsampled layout: off
- **Runtime:** [`FoveationRequester`](Assets/FoveationRepro/Scripts/FoveationRequester.cs) follows the [Unity OpenXR docs](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.19/manual/features/foveated-rendering/foveated-rendering-srp-api.html). It finds the running `XRDisplaySubsystem` and sets:
  ```csharp
  display.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None; // fixed, no gaze
  display.foveatedRenderingLevel = 1f;                                              // maximum
  ```
  It re-applies these every frame by default (`reapplyEveryFrame`), which rules out the runtime or URP resetting the level after startup.

### 3. Floating HUD: requested vs. read back

[`FoveationHud`](Assets/FoveationRepro/Scripts/FoveationHud.cs) is a world-space panel about 1.4 m in front of the user that lazily follows the head. It shows:

| Section | Values |
|---|---|
| Requested | level, flags, whether and how often it was applied |
| Read back | Foveated Rendering feature enabled, foveation method, `SystemInfo.foveatedRenderingCaps`, `XRDisplaySubsystem.foveatedRenderingLevel` / `foveatedRenderingFlags`, the level the runtime reports through `XR_FB_foveation` (the OpenXR package's own `FBGetFoveationLevel`, called by reflection), dynamic foveation state |
| OpenXR extensions | whether the runtime enabled `XR_FB_foveation`, `XR_FB_foveation_configuration`, `XR_FB_foveation_vulkan`, `XR_FB_swapchain_update_state`, `XR_META_foveation_eye_tracked`, `XR_META_vulkan_swapchain_create_info` |
| Environment | OpenXR runtime name/version, plugin and Unity versions, graphics API, render pipeline, and the current value of `xr.disable_foveation` as the app sees it |
| Verdict | **SHOULD be active** when everything above lines up. Otherwise **NOT active**, with the reasons listed |

The same report is written to logcat every 5 s, so it can be read without wearing the headset:

```powershell
adb logcat -s Unity | Select-String -Context 0,40 "\[FoveationRepro\]"
```

### 4. Device setting: `xr.disable_foveation`

On this device foveation is turned off by a system property that ships enabled:

```
/vendor/build.prop:  xr.disable_foveation=1
```

It is a system property (`getprop`), **not** a `settings` entry. `settings get global|secure|system xr.disable_foveation` all return `null`.

Check it:

```powershell
adb shell getprop xr.disable_foveation
```

Clear it (works from the `adb shell` user on this userdebug build, no root needed):

```powershell
adb shell setprop xr.disable_foveation 0
```

Because the default lives in `/vendor/build.prop`, **it goes back to `1` after every reboot.** Set it again after each reboot, then (re)start the app.

### 5. Test environment for seeing blur

[`FoveationTestEnvironment`](Assets/FoveationRepro/Scripts/FoveationTestEnvironment.cs) builds this at runtime:

- **A ring of 16 panels** (radius 2.2 m) all around the user. Each panel is a 72-spoke Siemens star over bands of 1-, 2- and 4-pixel checkers and stripes, with a red border.
- **A floor grid** with 1-px lines every 16 px and heavier lines every metre.

Fixed foveation lowers the shading rate toward the edges of each eye's view. With it on, the fine detail in your peripheral vision turns blocky or smeared, and the panel straight ahead stays sharp. Look ahead, note how the side panels look, then turn your head so a side panel is in the centre and compare. With foveation off, every panel looks equally sharp wherever it is in view.

The scene is `Assets/Scenes/FoveationRepro.unity`, a copy of the template's `SampleScene` with its tutorial `UI` and `Environment` objects turned off. It is the only scene in the build.

## Build and run

1. Open the project in Unity 6000.6.8f1 with the Android build target.
2. `adb shell setprop xr.disable_foveation 0`
3. Build `Assets/Scenes/FoveationRepro.unity` (development build) to `Builds/FoveationRepro.apk`, then:
   ```powershell
   adb install -r Builds/FoveationRepro.apk
   ```
   ```powershell
   adb shell am start -n com.example.foveationrepro/com.unity3d.player.UnityPlayerGameActivity
   ```
   If this fails with "Activity class … does not exist" straight after a reboot, unlock the headset first.
4. Read the HUD.
5. Enable the **XR Performance Data Overlay** in **XR Engine Hub** or in **Developer Options**, and check the foveation level it reports.

## Results

### Run 1 (2026-11-24): `xr.disable_foveation=0`, fixed foveation at level 1

**The app side reports that foveation should be active.** HUD / logcat report:

```
Requested
  foveatedRenderingLevel = 1.00
  flags = None (fixed)
Read back
  FoveatedRenderingFeature: ON
  Method: SRPFoveation
  SystemInfo.foveatedRenderingCaps: FoveationImage, ModeChangeOnlyBeforeRenderTargetSet
  display.foveatedRenderingLevel: 1.00
  display.foveatedRenderingFlags: None
  Runtime FB level (xrGet): High
  Dynamic foveation: False
OpenXR extensions enabled
  ON  XR_FB_foveation
  ON  XR_FB_foveation_configuration
  ON  XR_FB_foveation_vulkan
  ON  XR_FB_swapchain_update_state
  OFF XR_META_foveation_eye_tracked   (expected: eye tracking turned off for fixed foveation)
  ON  XR_META_vulkan_swapchain_create_info
Environment
  Runtime: Android XR 1.0.0 (API 1.1.62)
  Unity 6000.6.8f1, Vulkan, URP ("Standalone Performant Preset")
  xr.disable_foveation: prop=0
Verdict: foveation SHOULD be active
```

Logcat shows no foveation-related errors or warnings from Unity or the runtime.

**The device shows that foveation is not active:**

| Check | Result |
|---|---|
| Visual check of the test panels | **No foveation.** Peripheral panels are as sharp as the centre one. |
| XR Performance Data Overlay | **Foveation level is reported as None.** |

**Expected:** with the Foveated Rendering feature enabled, `foveatedRenderingLevel = 1`, the runtime reporting level `High`, and `xr.disable_foveation=0`, the device should apply fixed foveation, and the overlay should show a foveation level above None.

**Actual:** the app and the OpenXR runtime report foveation at its highest level, but the system does not apply it.
