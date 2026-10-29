# Chopper game â€” handoff (27 Oct 2026)

Read this first, then `Docs/MultiplayerOverhaulPlan.md` (full plan + progress log) and `CLAUDE.md` (rules for AI sessions).

## 1. Where things are

| Item | Value |
|---|---|
| Branch | `v2` (all new work). `main` is the old 2023 game. |
| Engine | Unity 6.6 (6000.6.7f1), URP (Render Graph, Forward+) |
| Netcode | Netcode for Entities + Unity Physics. Server-authoritative, owner-predicted choppers. |
| Code | `Assets/Chopper` (assemblies in `Assets/Chopper/README.md`). Old game code: `Assets/Scripts` (menus, not ported yet) and `Assets/Scripts/_LegacyNGO~` (not compiled, reference only). |
| Play scene | **`Assets/Scenes/TrainingRange.unity`**. Its subscene `TrainingRange_Gameplay` holds the gameplay content. Other scenes start only a local world (no server, no chopper). |
| Setup | **Chopper â†’ Setup** window: *Do all*, then *Validate*. Fix what it lists. |
| Working with Claude | Cloud sessions cannot run Unity (the network blocks Unity downloads), so code is written without compiling. To compile and test with Claude, run Claude Code on your PC with MCP for Unity (`com.coplaydev.unity-mcp`, port 6400), or run the Unity CLI there. |

## 2. What works (tested by the user in the editor)

- Local server + client in one editor; the chopper spawns and flies (Battlefield-style Little Bird feel).
- UI Toolkit HUD: central flight frame (ring, roll horizon, sway), speed, altitude, hull, dash, reticle, hit/kill markers, damage direction ring, match timer, K/D, kill feed, end banner, out-of-bounds warning.
- Sound and effects from the old game's assets.

## 3. What is built but not fully tested

Written without the editor; it compiles, but each needs a play check.

- **Combat:**
  - Hitscan nose gun with lag compensation (200 ms cap).
  - Rockets with splash damage.
  - Server health, crash damage and out-of-bounds damage.
  - Kill credit, respawn after 3 s.
  - FFA match: score limit, timer, post-game, next match.
- **Loadout data:** 4 chassis with flight tuning, 23 modules, 4 weapon definitions. `LoadoutRules` validate and compute stats. The prefab bakes a default Gunship loadout.
- **Services (code only):** sign-in (Steam ticket or anonymous), a matchmaking interface (local stub only), a VContainer root scope. Steam code compiles only when Steamworks.NET is installed.
- **Look:** the setup copies the old chopper's materials, rotor spin, spot light and smoke onto the ghost. This is a placeholder until the modular sockets exist.

## 4. Architecture in one page (plan section 1c)

```
Simulation (ECS, client + server)          Presentation (client)                 UI (UI Toolkit)
  Flight / Combat / Match / Session   RPCs   PresentationEventSystem  messages    ChopperHudView
  Loadout + Definitions (assets)    â”€â”€â”€â”€â”€â”€â–º  (only RPC reader)       â”€â”€â”€â”€â”€â”€â”€â”€â–º    UISoundBinder
                                             PresentationContext â—„â”€â”€ PresentationBridge (scene)
                                             (audio, effects, HUD models, camera, events)
Config root: GameConfig asset â†’ loadout catalog, sound library, effects, music, panel settings, HUD layout
Editor: Chopper â†’ Setup (create/repair config, prefab, scene, subscene; validate)
```

Rules:
- Settings hang off `GameConfig`.
- Content is definition assets.
- Presentation systems derive from `PresentationSystemBase`.
- Simulation events reach presentation only as messages.
- No singletons and no static state.
- The server decides health, hits and score.
- New setup steps and checks go into the Setup window.

**Unity 6.6 traps:**
- Managed components are deprecated: use `UnityObjectRef<T>`.
- Use `FindAnyObjectByType`, not `FindFirstObjectByType`.
- `UpdateAfter(PhysicsSystemGroup)` from another group is ignored: put post-physics systems in `PhysicsSystemGroup` with `OrderLast`.
- `PhysicsWorldHistorySingleton` needs the `Unity.NetCode.Physics` assembly reference.
- **Stale Burst code (seen 28 Oct, twice):** also after `ChopperInput` changes (input buffer garbled: bools lost, chopper ignores input). after a field is added to a struct that a Burst job reads (e.g. `ChopperFlightConfig`), the old compiled job can keep running with the old layout: forces silently wrong (chopper "has no force"). Fix: stop play, delete `Library/BurstCache` (or close Unity first), recompile. Quick check: turn off Jobs â†’ Burst â†’ Enable Compilation; if the bug goes away, it is the cache.

## 4b. Newest work (27 Oct, not yet compiled)

- Targeting, heat-seeking missiles (missile ghosts with predicted spawn), flares, pylon weapon cycling, lock and threat HUD, tones. See plan section 3b, "Built".
- After pulling: Chopper â†’ Setup â†’ **Do all** (makes the missile prefab, weapon assets, effect/sound slots, re-bakes the default loadout, makes targets pre-spawned ghosts), then Validate.
- Controls: RMB / E / LB fire the selected pylon; wheel / Tab / D-pad right switch pylon; Space / triggers drop flares.
- **Modular sockets runtime** (plan section 5, "Modular build", list "Built"):
  - Loadout goes on the ghost as module ids; client and server both apply it (`LoadoutApplySystem`, `LoadoutBuilder`).
  - Each player's choice (`LoadoutChoice`, set later by the hangar) goes with the go-in-game request; the server validates it.
  - Module art follows the sockets (`ModuleVisualSystem`, pooled).
  - Setup â†’ *Do all* adds placeholder sockets to `ChopperGhost` (under "Sockets"), fills each chassis (prefab + defaults), links PolygonMilitary art to modules, and configures the spawner.
  - Socket positions are placeholders: nose and pylons from the old weapon slots; the others are guesses. Move them in the prefab (green gizmo cubes).
  - The old `Assets/Prefabs/Weapons` prefabs are not used: they have the old NGO `Weapon` script (missing).
- **Retro CRT HUD look** (plan section 3b, "Retro HUD look â€” built"):
  - The HUD panel renders into a texture; a URP renderer feature (`HudLookRendererFeature`, shader `Hidden/Chopper/HudLook`) draws it over the game with glow, scanlines, curvature, colour split, vignette, noise and flicker. The game view is not changed.
  - `HudLookSettings` on `GameConfig`: presets Clean (plain UI, no pass), Vector, CRT 84 (default); intensities; gameplay reactions.
  - Gameplay drives it (`HudLookModel`): glitch on damage, flicker at low hull, pulse on your lock, noise as a missile closes in, drop-out on death and boot-up on spawn. `HudLook.Disrupt` is ready for EMP and hack.
  - Player options (saved in PlayerPrefs): `HudLook.Preset` and `HudLook.ReduceEffects` (no flicker, weaker glitches), set in the options overlay.
  - Phosphor trail: moving HUD parts leave a short fading ghost (two HUD-size history textures, ping-pong, in the same renderer feature). `Persistence` / `PersistenceTime` per preset.
  - Setup step 1 makes the settings and adds the feature to every URP renderer; Validate checks them.
- **World markers and rocket lead** (plan section 3b, "Markers, lead, cluster â€” built"): a marker on every other chopper (red diamond) and ground target (amber square), closest 24, distance under it, health bar when near, filled when it is your lock target, edge arrow when off screen. A lead circle shows where to aim the selected rocket pod (lock target, else the target nearest the nose).
- **Cluster missiles** (module 116 "Cluster Missiles", `Weapon_ClusterMissile`): a guided missile that splits into 8 submunitions near the target or after 1.8 s. Submunitions are server rockets; one RPC with a seed lets clients draw them. Any guided missile definition can be a cluster (Cluster section, 0 = off).
- **Swarm Pod** (module 117, `Weapon_SwarmMissiles`): one trigger pull with a lock fires 10 small seekers, one every 0.06 s, fanned 20Â°; 3 of 10 ignore flares. Any guided missile can have a salvo (Salvo section on the definition).
- **Options overlay** (Escape / gamepad Start): HUD look preset, reduce flicker / effects, 5 volume sliders. Controls: mouse left/right = turn (aim style, default) or roll (classic), sensitivity, invert pitch (`InputOptions`, saved). While open: cursor free, flight input neutral (`PlayerInputSettings` singleton, written by `InputSettingsSystem`). First piece of the UI Toolkit menu port.
- **Prediction smoothing** (own chopper): server corrections fade out over a few ticks instead of a snap (`ChopperSmoothingAction`, Netcode `GhostPredictionSmoothing` on `LocalTransform`). Each correction removes 25% of the position error and 35% of the rotation error; errors over 10 m snap (respawn). Velocity is not smoothed. Tune on `ChopperAuthoring` â†’ "Network smoothing". Missiles are not smoothed. The Simulation assembly now allows unsafe code (needed for the smoothing callback).
- **Flight feel (28 Oct, tested in play)**: Battlefield collective. W/S lift along the rotor (`CollectiveUp` 12 m/sÂ², `CollectiveDown` 10): level W rises about 10 m/sÂ². Nose down gives speed (`TiltThrust` 1.6, `PitchThrust` 1): 20Â° tilt â‰ˆ 9 m/sÂ² forward with height held. Bank curves turns. Mouse X rolls by default (Battlefield); "aim style" yaw is in the options. A/D yaw without roll banks into the turn, blended smoothly. Camera follows 85% of the roll on a ~0.25 s delay (`ChopperCameraRig.rollFollow` / `rollSharpness`). Tune per chassis on `Chassis_*.asset` â†’ Flight.
- **Camera** (`ChopperCameraRig` + `CameraSettings` on `GameConfig`, `Data/Camera/CameraSettings.asset`): our own system, not Cinemachine (owner decision). Views list (chase, cockpit = the old game's cockpit camera); C / gamepad Y cycles them (saved; not while the menu is open). Blends position, rotation, FOV and near clip between views; per-view noise; hit shake; speed FOV and pull-back; snaps on respawn/teleport. Add a view = add a list entry (Chase or Mounted).
- **Feel pass (28 Oct)**:
  - Boost replaces dash: hold Shift / gamepad B. A tank (3.5 s full, refills in 5â€“7.5 s by chassis after 0.8 s; Boost Engine module refills faster). Kick +9 m/s, then 24 m/sÂ² where the nose aims (down dives, up climbs), forward drag Ã— 0.55. Tested: 120 â†’ 430 km/h in 3.4 s, height held. `ChopperFlightState.Boosting` / `BoostUsed` are ghost fields; boosting also raises seeker heat.
  - Rotor spools to W/S (`CollectiveResponse` 6/s): a little weight.
  - Drag now acts in the heading frame (yaw only), not the tilted body: fast nose-down flight no longer sinks.
  - Camera weight and boost (`CameraSettings`): inertia lag from acceleration; boost spring (FOV +14Â°, pull-back, punch overshoot), kick shake, engine rumble, HUD glitch on the kick. `SpeedEffects`: runtime post volume (bloom with clamp, speed chromatic aberration, boost vignette, exposure flash; Stop NaN on). No lens distortion: toggling it as the boost spring crossed zero gave flat one-colour frames (the "flash and pulse" bug) and camera-space speed lines (`Hidden/Chopper/SpeedLine`). No motion blur (it smears the chopper).
  - Audio: boost kick = deep distant explosions, roar = `loop_rocket2` at pitch 0.5; rotor strains with collective and boost.
  - HUD: segmented `ArcGauge` arcs on the flight ring (boost left, hull right); boosting turns the ring and arc magenta, enlarges the speed readout and shows ">> BOOST".
- Decisions taken (owner): Battlefield-like missile ghosts; auto lock, some missiles cannot dumb-fire; cycle the selected pylon; one ghost per chassis; pooled GameObject module visuals; sockets before the CRT HUD.

### Editor steps after pulling
1. Chopper â†’ Setup â†’ **Do all**, then **Validate**. Fix what it lists.
2. Fix compile errors (nothing from 27 Oct is compiled yet). Send the console list to Claude if needed.
3. Open `TrainingRange.unity`, play. Check: chopper spawns, minigun / rocket pod / missile / flare art sits on the chopper, locks and missiles work, flares break locks, wheel switches pylon. Markers sit on the training targets; the lead circle shows with the rocket pod selected. Fit "Cluster Missiles" (change the default loadout or the chassis defaults) and fire it at a target: 8 bomblets.
4. HUD look: the HUD should glow, with scanlines and a slight curve. The phosphor trail is off on a `HudLookSettings` asset made before this change: set Persistence (0.5) and Persistence Time (0.12) on the CRT 84 profile, or delete the asset and run Do all. Take a hit: the HUD glitches. Die: it drops out. If the HUD is gone or black: set the preset to Clean on `Data/Hud/HudLookSettings` (default preset) or clear the PlayerPrefs key `Chopper.HudLook.Preset`, and send the console to Claude. Check the HUD edges for dark fringes (alpha mode).
5. Press Escape: the options overlay opens, the cursor shows, the chopper stops taking input. Change HUD look and volumes; press Escape again. Fit "Swarm Pod", lock a target, fire: 10 missiles fan out.
6. Runtime skins (done 29 Oct in the editor: graph made by `Tools/PartForge/make_skin_graph.py`, step 5 run, tests pass, skin id checked in play). To look: Setup → **Open skin preview scene**, select **Kit Preview**, change Skin and Method (Baked / Runtime).
   Kits (28 Oct, textures and skins): Setup step 5 again. It makes `Art/Kits/<kit>/Materials/<kit>_<Set>_<skin>.mat` and `<kit>_Skins.asset`, and links Kit02's skins into the game config. Drag a `Chassis_Gunship` model into a scene, try a few skin materials on it, check the normal map (no inverted bumps) and the edge wear. If an old `<kit>_Kit.mat` exists, delete it (no longer used).
7. Move sockets in `ChopperGhost` â†’ Sockets if the art sits in the wrong place, then run *Do all* again (it does not move sockets).
8. Network feel: Multiplayer Play Mode or a build, Network Simulator 150 ms / 2% loss. Fly hard turns and dashes. Small corrections should be soft, not jumps. Too floaty: raise the smoothing values; still jumpy: lower them, or look for a client/server mismatch in the flight code.
9. Run edit-mode tests (Window â†’ General â†’ Test Runner).
10. If tick-batching or Burst messages come back: clear `Library/BurstCache`.

## 5. Open problems

| Problem | Likely cause | Next action |
|---|---|---|
| "Server Tick Batching" warnings (1.25â€“1.75 ticks/frame) | Editor below 60 FPS; maybe the Burst cache is stale | Clear `Library/BurstCache`, check Game view Stats FPS, test in a build, profile if it stays |
| Burst "not a known entry point" messages | Stale Burst cache after big code changes | Same: clear `Library/BurstCache` |
| Not tested with more than one player | â€” | Multiplayer Play Mode, 4 players, Network Simulator 150 ms / 2% loss |
| MessagePipe without a DI container (`PresentationEvents`) | API not checked in an editor before | Watch for errors on first message |

## 6. Next steps (recommended order)

Next session: continue with this order (step 1 onward). The feel pass and style guide work of 28 Oct is done; its follow-ups (feel tuning, match-end, lobby and server list screens from Figma) are the backlog in plan section 7d.

1. **Finish Phase 2 (needs Unity):** the 4-player network test, the tick-batching fix, and the edit-mode tests.
2. **Phase 4: modular build.** Plan section 5, "Modular build: lego sockets":
   - done (not yet compiled): sockets, module visuals, the loadout on the ghost, each player's loadout at spawn;
   - done: PartForge Kit01 (neon) and Kit02 (dystopia, the owner's direction; default), 4 chassis + 23 modules each; art direction in `Tools/PartForge/ART_DIRECTION.md`; Setup step 5 imports the kits;
   - done (28 Oct, not yet in Unity): PartForge alignment check (`generate.py --audit`, 0 problems), review studio lighting and four-view sheets, baked texture sets (UVs, normal, occlusion, wear and grime) and 10 skins (`partforge/skins.py`; Unity: one URP Lit material per set and skin, `SkinLibrary` per kit, linked in `GameConfig`);
   - done (28 Oct, not compiled): runtime skin shader for A/B against the baked skins: `Art/Shaders/ChopperSkin.hlsl` (Shader Graph custom function; the graph is made by hand, see `Art/Shaders/README.md`), skin table texture from `SkinLibrary` looks (`SkinTable.Encode`, test `SkinTableTests`), one runtime material per texture set, `SkinPreview` component (Baked / Runtime, next skin, flip timer), skin id from `LoadoutChoice.SkinId` â†’ `GoInGameRequest` â†’ `RequestedLoadout` â†’ `ChopperSkin` ghost field, `SkinApplySystem` (per-entity `_SkinIndex` override) and module visuals (property block);
   - next for skins: decide baked or runtime after the A/B; the "Build chassis from kit" step uses that material; skin choice in the hangar; the server checks skin unlocks (progression);
   - next: check Kit02 in Unity (step 5, then look at a model with each skin material); "Build chassis from kit" Setup step and module visual links; rotor spin on rotor modules; rotor module on every rotor socket at the socket scale (Lifter: tandem / coaxial / quad); Lifter colliders;
   - then the green wireframe hangar (it sets `LoadoutChoice`).
3. **Targeting, guided weapons and the dynamic HUD** (plan section 3b):
   - done (not yet compiled): heat-seeker locks, missile ghosts (AA and AG), flares, lock brackets and warnings;
   - done (not yet compiled): retro 80s HUD look (HUD to a render texture, full-screen CRT pass, `HudLookSettings`, gameplay glitches);
   - done (not yet compiled): options toggle for preset / reduce effects; phosphor persistence;
   - next for the look: tune it in play; per-element Shader Graph UI materials; same look for menus and hangar;
   - done (not yet compiled): cluster missiles, world markers, rocket lead indicator;
   - done (not yet compiled): swarm micro-missiles (Swarm Pod);
   - next: team colours on markers when TDM exists; objective markers (nuke, bases); the other weapons in the table (railgun, EMP, chain lightning...).
   It can run in parallel with step 2: the weapons are modules.
4. **Phase 3: services:**
   - Steamworks.NET and the Dashboard Steam provider;
   - Multiplayer Services sessions with a Cloud Code allocator for Edgegap;
   - a dedicated server build;
   - party and rejoin.
5. **Phase 6: modes:** TDM, then Capture the Nuke, then bots.
6. **Phase 5: progression:** Cloud Code grants, Cloud Save, unlocks, leaderboards and Steam achievements.
7. **Any time:**
   - port the old menus to UI Toolkit and delete `Assets/Scripts` (the options overlay `UI/Options` is the first piece; reuse its style);
   - export the `.blend` models to `.fbx`;
   - delete or rebuild the old scenes.

## 7. Owner preferences

- The world is a **gritty, kinetic cyberpunk**: keep the old game's feel wherever possible. Examples: the HUD that moves with the chopper, the pitched explosion gun sound, lasers as a late unlock.
- The hangar and UI are green cyberpunk, with a 3D wireframe chopper. The camera focuses on each socket.
- HUD: retro 80s look â€” CRT / vector-display shader or post-processing over the HUD (plan section 3b).
- Weapons: heat-seeking missiles (air and ground), cluster missiles and other cool cyberpunk weapons; a dynamic HUD overlay with targeting (plan section 3b).
- Choppers will become fully modular ("lego" modules on chassis sockets). The current chopper is a placeholder.
  - Target modularity (owner reference): https://www.fab.com/listings/a090fe12-34f9-4eab-9ca8-5f979a29e01f
  - Parts are generated procedurally in Blender (`Tools/PartForge`, see its README), not Asset Forge (its blocks look too childish). Kits in `Assets/Chopper/Art/Kits`. To tweak: open `Tools/PartForge/blend/Kit02.blend` in Blender 5.x, allow scripts, N â†’ PartForge. Plan section 5, "Part kits and authoring pipeline".
- Replies to the owner: short dot points, in ASD-STE100 Simplified English.

## 8. Key files

| Area | Files |
|---|---|
| Bootstrap / spawn | `Simulation/ChopperBootstrap.cs`, `Simulation/Session/*` (incl. dev `SessionDiagnosticsSystem`) |
| Flight | `Simulation/Flight/ChopperFlightSystem.cs`, `FlightTuning.cs`, `ChopperAuthoring.cs`, `ChopperPredictionSmoothing.cs` |
| Combat | `Simulation/Combat/*` (`HitscanFireSystem`, `RocketSystem`, `ApplyDamageSystem`, `EnvironmentDamageSystem`) |
| Match | `Simulation/Match/MatchScoreSystem.cs` |
| Loadout | `Simulation/Loadout/*` (`LoadoutCode`, `LoadoutBuilder`, `LoadoutApplySystem`, `SocketAuthoring`), `Simulation/Definitions/*`, data in `Assets/Chopper/Data/Loadout` |
| Modules (visual) | `Presentation/Visuals/ModuleVisualSystem.cs`, setup in `Editor/Setup/SocketAssets.cs` |
| Presentation | `Presentation/Bridge/*`, `Events/*`, `Feedback/*`, `Audio/*`, `Effects/*`, `Hud/*`, `Visuals/*` |
| UI | `UI/Hud/ChopperHud.uxml/.uss`, `ChopperHudView.cs`, theme in `UI/Theme` |
| HUD look | `Presentation/Hud/HudLook*.cs`, `HudLook.shader`; settings `Data/Hud/HudLookSettings.asset`; setup `Editor/Setup/HudLookAssets.cs` |
| Editor | `Editor/Setup/*` (window, asset factories, validator) |
| Markers | `Presentation/Hud/WorldMarker*.cs`, `UI/Hud/WorldMarkerView.cs` |
| UI components | HUD and Options use Unity 6.6 `PanelRenderer` (not `UIDocument`); views bind in `RegisterUIReloadCallback` and rebind when the panel rebuilds. A `UIDocument` next to a `PanelRenderer` shows the UI twice (Validate checks it). |
| Options | `UI/Options/*` (overlay), `Presentation/Bridge/InputSettingsSystem.cs`, `Simulation/Input/PlayerInputSettings.cs` |
| Tests | `Tests/EditMode/*` (HUD models, HUD look, markers, cluster, health, loadout rules, loadout code, guidance, audio) |
