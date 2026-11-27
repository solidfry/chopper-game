# UniStorm overrides

Our changes to `Assets/UniStorm Weather System`. An asset update can overwrite them: before you update, note this list;
after, check each item. Updated 27 Nov 2026 after the UniStorm update (base package, then
`URP Support/UniStorm URP Support.unitypackage`, the 19 Nov one: all 51 scripts, Render Graph).

## How to update UniStorm

1. Update the asset (Package Manager > My Assets). It brings the **Built-in** scripts: `UniStormSystem` loses
   `urpForwardRendererData` and `AddNecessaryRendererFeatures.cs` fails to compile.
2. Import `URP Support/UniStorm URP Support.unitypackage` by hand (not the May "Render Graph" or
   "Compatibility Mode" ones). Untick `URP Support/Scene`.
3. Delete `PostProcessing/` if it comes back (PostProcessing v1, nothing uses it).
4. Check the list below, then: compile, play Map_City01 at Night in Rain (EnvironmentSettings editor preview).

## Must keep (the game breaks or warns without them)

| File | Change | Why |
|---|---|---|
| `Scripts/UniStorm.asmdef` | Assembly `UniStorm` (refs URP Runtime, RP Core, UI, InputSystem) | `Chopper.Presentation.UniStorm.asmdef` references it. |
| `Scripts/Editor/UniStorm.Editor.asmdef`, `Scripts/Effects/Editor/UniStorm.Editor.asmref` | Editor assembly `UniStorm.Editor` | Needed once `UniStorm.asmdef` exists. |
| `Scripts/Effects/RendererFeaturesCheck.cs` | `Check()` returns at once (marked `Chopper edit`; `#pragma warning disable CS0162` on line 1) | New in the 27 Nov version: logs an error every match when the renderer has no `UniStormCloudsRendererFeature`. Our renderer has no UniStorm features on purpose (Buto fog). |
| `Scripts/System/UniStormPool.cs` | `PoolMember.myPool` is `[System.NonSerialized]` | Kept by the 27 Nov version; check after an update. |

## Fixed upstream in the 27 Nov version (no edit needed now)

- Sun, moon and lightning `shadowResolution` lines are commented out (we had guarded them for URP).
- `Light.GetLights` replaced with `FindObjectsByType<Light>` in the fog and sun shaft passes.
- `FindFirstObjectByType` -> `FindAnyObjectByType` in `AddNecessaryRendererFeatures.cs`.

## Cleanup (safe to lose, but warnings come back)

- `#pragma enable_d3d11_debug_symbols` removed from `UniStormAtmosphericFog.shader`, `UniStormCloudShadows.shader`,
  `UniStormSunShafts.shader` (5 passes), `Shaders/UniStorm Moon.shader`, `UniStorm Stars.shader`, `UniStorm Sun.shader`.
- `Resources/Clouds/detailNoise.asset` re-saved (Texture3D version 2 -> current; Unity warned at load).
- `Materials/Weather/Lightning Bolt.mat`: unused `_BaseMap` slot pointed at the old `Lightning Bolt.png` GUID
  (`7d526f28...`, changed to `de21555a...` in the 27 Nov version); now the new one.
- `Demo Source/Effect Profile/` (PostProcessing v1 profile) stays deleted.
- The 24 Nov material keyword cleanup (`_FADING_ON`, invalid keywords) was overwritten by the 27 Nov materials; redo only
  if Unity warns about them.

## Not ours

- `Materials/Clouds/UniStorm Volumetric Clouds.mat` changes in the working tree on every play: never commit that churn.
- `Chopper.Presentation.UniStorm` (`Assets/Chopper/Presentation/UniStorm`) is our adapter, outside the UniStorm folder.
- The folder must stay at `Assets/UniStorm Weather System` (its scripts hard-code the path).
- `Docs/UniStormOverrides.patch` is the diff from the first import to the pre-update version (history only).
