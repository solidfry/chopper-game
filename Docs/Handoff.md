# Chopper game — handoff (updated 29 Oct 2026)

Read this first, then `Docs/MultiplayerOverhaulPlan.md` (full plan + progress log) and `CLAUDE.md` (rules for AI sessions).

## 1. Where things are

| Item | Value |
|---|---|
| Branch | `v2` (all new work). `main` is the old 2023 game. |
| Engine | Unity 6.6 (6000.6.7f1), URP (Render Graph, Forward+) |
| Netcode | Netcode for Entities + Unity Physics. Server-authoritative, owner-predicted choppers. |
| Code | `Assets/Chopper` (assemblies in `Assets/Chopper/README.md`). Old game code: `Assets/Scripts` (menus, not ported yet) and `Assets/Scripts/_LegacyNGO~` (not compiled, reference only). |
| Play scene | **`Assets/Scenes/TrainingRange.unity`**. Its subscene `TrainingRange_Gameplay` holds the gameplay content. Other scenes start only a local world (no server, no chopper). |
| Setup | **Chopper → Setup** window: *Do all*, then *Validate*. Fix what it lists. |
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
  Loadout + Definitions (assets)    ──────►  (only RPC reader)       ────────►    UISoundBinder
                                             PresentationContext ◄── PresentationBridge (scene)
                                             (audio, effects, HUD models, camera, events)
Config root: GameConfig asset → loadout catalog, sound library, effects, music, panel settings, HUD layout
Editor: Chopper → Setup (create/repair config, prefab, scene, subscene; validate)
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
- **Stale Burst code (seen 28 Oct, twice):** also after `ChopperInput` changes (input buffer garbled: bools lost, chopper ignores input). after a field is added to a struct that a Burst job reads (e.g. `ChopperFlightConfig`), the old compiled job can keep running with the old layout: forces silently wrong (chopper "has no force"). Fix: stop play, delete `Library/BurstCache` (or close Unity first), recompile. Quick check: turn off Jobs → Burst → Enable Compilation; if the bug goes away, it is the cache.

## 4b. Newest work (27 Oct, not yet compiled)

- Targeting, heat-seeking missiles (missile ghosts with predicted spawn), flares, pylon weapon cycling, lock and threat HUD, tones. See plan section 3b, "Built".
- After pulling: Chopper → Setup → **Do all** (makes the missile prefab, weapon assets, effect/sound slots, re-bakes the default loadout, makes targets pre-spawned ghosts), then Validate.
- Controls: RMB / E / LB fire the selected pylon; wheel / Tab / D-pad right switch pylon; Space / triggers drop flares.
- **Modular sockets runtime** (plan section 5, "Modular build", list "Built"):
  - Loadout goes on the ghost as module ids; client and server both apply it (`LoadoutApplySystem`, `LoadoutBuilder`).
  - Each player's choice (`LoadoutChoice`, set later by the hangar) goes with the go-in-game request; the server validates it.
  - Module art follows the sockets (`ModuleVisualSystem`, pooled).
  - Setup → *Do all* adds placeholder sockets to `ChopperGhost` (under "Sockets"), fills each chassis (prefab + defaults), links PolygonMilitary art to modules, and configures the spawner.
  - Socket positions are placeholders: nose and pylons from the old weapon slots; the others are guesses. Move them in the prefab (green gizmo cubes).
  - The old `Assets/Prefabs/Weapons` prefabs are not used: they have the old NGO `Weapon` script (missing).
- **Retro CRT HUD look** (plan section 3b, "Retro HUD look — built"):
  - The HUD panel renders into a texture; a URP renderer feature (`HudLookRendererFeature`, shader `Hidden/Chopper/HudLook`) draws it over the game with glow, scanlines, curvature, colour split, vignette, noise and flicker. The game view is not changed.
  - `HudLookSettings` on `GameConfig`: presets Clean (plain UI, no pass), Vector, CRT 84 (default); intensities; gameplay reactions.
  - Gameplay drives it (`HudLookModel`): glitch on damage, flicker at low hull, pulse on your lock, noise as a missile closes in, drop-out on death and boot-up on spawn. `HudLook.Disrupt` is ready for EMP and hack.
  - Player options (saved in PlayerPrefs): `HudLook.Preset` and `HudLook.ReduceEffects` (no flicker, weaker glitches), set in the options overlay.
  - Phosphor trail: moving HUD parts leave a short fading ghost (two HUD-size history textures, ping-pong, in the same renderer feature). `Persistence` / `PersistenceTime` per preset.
  - Setup step 1 makes the settings and adds the feature to every URP renderer; Validate checks them.
- **World markers and rocket lead** (plan section 3b, "Markers, lead, cluster — built"): a marker on every other chopper (red diamond) and ground target (amber square), closest 24, distance under it, health bar when near, filled when it is your lock target, edge arrow when off screen. A lead circle shows where to aim the selected rocket pod (lock target, else the target nearest the nose).
- **Cluster missiles** (module 116 "Cluster Missiles", `Weapon_ClusterMissile`): a guided missile that splits into 8 submunitions near the target or after 1.8 s. Submunitions are server rockets; one RPC with a seed lets clients draw them. Any guided missile definition can be a cluster (Cluster section, 0 = off).
- **Swarm Pod** (module 117, `Weapon_SwarmMissiles`): one trigger pull with a lock fires 10 small seekers, one every 0.06 s, fanned 20°; 3 of 10 ignore flares. Any guided missile can have a salvo (Salvo section on the definition).
- **Options overlay** (Escape / gamepad Start): HUD look preset, reduce flicker / effects, 5 volume sliders. Controls: mouse left/right = turn (aim style, default) or roll (classic), sensitivity, invert pitch (`InputOptions`, saved). While open: cursor free, flight input neutral (`PlayerInputSettings` singleton, written by `InputSettingsSystem`). First piece of the UI Toolkit menu port.
- **Prediction smoothing** (own chopper): server corrections fade out over a few ticks instead of a snap (`ChopperSmoothingAction`, Netcode `GhostPredictionSmoothing` on `LocalTransform`). Each correction removes 25% of the position error and 35% of the rotation error; errors over 10 m snap (respawn). Velocity is not smoothed. Tune on `ChopperAuthoring` → "Network smoothing". Missiles are not smoothed. The Simulation assembly now allows unsafe code (needed for the smoothing callback).
- **Flight feel (28 Oct, tested in play)**: Battlefield collective. W/S lift along the rotor (`CollectiveUp` 12 m/s², `CollectiveDown` 10): level W rises about 10 m/s². Nose down gives speed (`TiltThrust` 1.6, `PitchThrust` 1): 20° tilt ≈ 9 m/s² forward with height held. Bank curves turns. Mouse X rolls by default (Battlefield); "aim style" yaw is in the options. A/D yaw without roll banks into the turn, blended smoothly. Camera follows 85% of the roll on a ~0.25 s delay (`ChopperCameraRig.rollFollow` / `rollSharpness`). Tune per chassis on `Chassis_*.asset` → Flight.
- **Camera** (`ChopperCameraRig` + `CameraSettings` on `GameConfig`, `Data/Camera/CameraSettings.asset`): our own system, not Cinemachine (owner decision). Views list (chase, cockpit = the old game's cockpit camera); C / gamepad Y cycles them (saved; not while the menu is open). Blends position, rotation, FOV and near clip between views; per-view noise; hit shake; speed FOV and pull-back; snaps on respawn/teleport. Add a view = add a list entry (Chase or Mounted).
- **Feel pass (28 Oct)**:
  - Boost replaces dash: hold Shift / gamepad B. A tank (3.5 s full, refills in 5–7.5 s by chassis after 0.8 s; Boost Engine module refills faster). Kick +9 m/s, then 24 m/s² where the nose aims (down dives, up climbs), forward drag × 0.55. Tested: 120 → 430 km/h in 3.4 s, height held. `ChopperFlightState.Boosting` / `BoostUsed` are ghost fields; boosting also raises seeker heat.
  - Rotor spools to W/S (`CollectiveResponse` 6/s): a little weight.
  - Drag now acts in the heading frame (yaw only), not the tilted body: fast nose-down flight no longer sinks.
  - Camera weight and boost (`CameraSettings`): inertia lag from acceleration; boost spring (FOV +14°, pull-back, punch overshoot), kick shake, engine rumble, HUD glitch on the kick. `SpeedEffects`: runtime post volume (bloom with clamp, speed chromatic aberration, boost vignette, exposure flash; Stop NaN on). No lens distortion: toggling it as the boost spring crossed zero gave flat one-colour frames (the "flash and pulse" bug) and camera-space speed lines (`Hidden/Chopper/SpeedLine`). No motion blur (it smears the chopper).
  - Audio: boost kick = deep distant explosions, roar = `loop_rocket2` at pitch 0.5; rotor strains with collective and boost.
  - HUD: segmented `ArcGauge` arcs on the flight ring (boost left, hull right); boosting turns the ring and arc magenta, enlarges the speed readout and shows ">> BOOST".
- **Cockpit view (30 Oct, plan section 7d "Cockpit view")**:
  - Cockpit is the default view (saved option `Chopper.Camera.DefaultView`, view name; C / gamepad Y only switches for the session). Hold Alt / R3 on a mounted view to look around (±130° yaw, −50..+65° pitch), release snaps back; mouse and pitch/roll axes are neutral meanwhile (`PlayerInputSettings.HeadLook`).
  - PartForge makes one interior per chassis (`parts.cockpit`, preview: `Tools/PartForge/cockpit_preview.py`): Gunship and Assault a minimal military cockpit (low faceted shell and instrument panel facing the eye, glare shield at about −18°, two square displays, side-stick, pedestal, thin frame, armoured seat); Scout the same with an open bubble (no frame in front, about −25°: the widest view, its edge); Lifter the busy 80s cabin dash. Socket `SOCKET_Eye_0` = the pilot's eye. `--audit` checks the cockpits too.
  - Cockpits use the kit palette maps directly (UV0 = palette cells, texture set "Palette", Unity material `Kit02_Palette`): baked sets cannot hold millimetre screen lines.
  - Setup step 6 links each kit cockpit and eye to its chassis (`ChassisDefinition.Cockpit` / `CockpitEye`). `CockpitVisualSystem` shows it for your own chopper only, in mounted views; mounted camera views sit at the chassis eye (`CameraView.FixedEye` turns that off). In a cockpit view your own hull (`DisableRendering` on its entities) and module art are not drawn: from inside, the hull's outside details (nuts, slits, plates) floated in the view; the pilot sees out through a "camera feed". `CameraView.ShowOwnChopper` keeps them (for a later gunner view). Tested 30 Oct in play: Gunship, Scout, Lifter; chase view unchanged.
  - Also changed for the cockpit view: the Gunship/Assault frame between the canopies is lower; the Lifter's glazing loft has no back cap (`Builder.loft(open_back=True)`). Both were made before the hide rule and are now optional.
  - Not yet: an options entry for the default view, the camera-feed boot-up pass, live data on the cockpit screens.
- Decisions taken (owner): Battlefield-like missile ghosts; auto lock, some missiles cannot dumb-fire; cycle the selected pylon; one ghost per chassis; pooled GameObject module visuals; sockets before the CRT HUD.

### Editor steps after pulling
1. Chopper → Setup → **Do all**, then **Validate**. Fix what it lists.
2. Fix compile errors (nothing from 27 Oct is compiled yet). Send the console list to Claude if needed.
3. Open `TrainingRange.unity`, play. Check: chopper spawns, minigun / rocket pod / missile / flare art sits on the chopper, locks and missiles work, flares break locks, wheel switches pylon. Markers sit on the training targets; the lead circle shows with the rocket pod selected. Fit "Cluster Missiles" (change the default loadout or the chassis defaults) and fire it at a target: 8 bomblets.
4. HUD look: the HUD should glow, with scanlines and a slight curve. The phosphor trail is off on a `HudLookSettings` asset made before this change: set Persistence (0.5) and Persistence Time (0.12) on the CRT 84 profile, or delete the asset and run Do all. Take a hit: the HUD glitches. Die: it drops out. If the HUD is gone or black: set the preset to Clean on `Data/Hud/HudLookSettings` (default preset) or clear the PlayerPrefs key `Chopper.HudLook.Preset`, and send the console to Claude. Check the HUD edges for dark fringes (alpha mode).
5. Press Escape: the options overlay opens, the cursor shows, the chopper stops taking input. Change HUD look and volumes; press Escape again. Fit "Swarm Pod", lock a target, fire: 10 missiles fan out.
6. Runtime skins (done 29 Oct in the editor: graph made by `Tools/PartForge/make_skin_graph.py`, step 5 run, tests pass, skin id checked in play). To look: Setup → **Open skin preview scene**, select **Kit Preview**, change Skin and Method (Baked / Runtime).
   Kits (28 Oct, textures and skins): Setup step 5 again. It makes `Art/Kits/<kit>/Materials/<kit>_<Set>_<skin>.mat` and `<kit>_Skins.asset`, and links Kit02's skins into the game config. Drag a `Chassis_Gunship` model into a scene, try a few skin materials on it, check the normal map (no inverted bumps) and the edge wear. If an old `<kit>_Kit.mat` exists, delete it (no longer used).
7. Kit chassis (done 29 Oct in the editor, step 6 run, play-tested): Setup → **6. Build chassis from kit** makes `Prefabs/Chassis/Chopper_<Chassis>.prefab` (variants of `ChopperGhost`: kit model, sockets on its `SOCKET_*` markers, box collider round the model) and links each chassis and the 23 kit modules. Press Play in TrainingRange: you fly the Gunship kit model. Sockets come from the model: to move one, move the marker in PartForge (`kits.py`) and export again. Nose lights and exhaust smoke were moved to the nose and engine sockets: move them in the prefab if they look wrong. `ChopperGhost` is now only the fallback.
8. Network feel: Multiplayer Play Mode or a build, Network Simulator 150 ms / 2% loss. Fly hard turns and dashes. Small corrections should be soft, not jumps. Too floaty: raise the smoothing values; still jumpy: lower them, or look for a client/server mismatch in the flight code.
   4-player test (done 29 Oct by Claude, measured): Multiplayer → Play Mode Tools: Num Thin Clients 3, simulator delay 75 ms (RTT about 170 ms), jitter 10, loss 2%. Thin clients fly by `ScriptedPilot` (`Session/ScriptedPilotSystems.cs`), each with the next chassis. To let it fly your chopper too, add a `ScriptedPilotOverride` singleton to the client world. Result over 75 s: 4 players, all 4 chassis, combat damage, no errors; 0–1 hitch in about 5,600 frames per chopper (own predicted and 3 remote); own chopper about 6 m ahead of the server at 50 m/s (expected). Set the tools back to 0 afterwards.
9. Run edit-mode tests (Window → General → Test Runner): 80 tests.
10. Console (29 Oct): 0 errors, 0 warnings after a clean compile and in play. Legacy assets with deleted scripts moved out of `Resources` to `Assets/Legacy` (builds packed them); the old Cinemachine camera scripts joined the retired legacy code in `Assets/Scripts/_LegacyNGO~`; TextMesh Pro font metas upgraded; Shapes patched (UAC0005). Note: the MCP console reader shows Burst `Debug.Log` lines ("[Client] Connected") as errors; Unity counts them as logs.
11. Hangar: Chopper → Setup → **7. Build hangar** (or Do all) after new kit models; "Open hangar scene", press Play, pick a loadout, LAUNCH.
12. Restart the editor once after pulling: incremental GC (Player settings) starts with the editor. If Burst "not a known entry point" messages come back: clear `Library/BurstCache`.

## 5. Open problems

| Problem | Likely cause | Next action |
|---|---|---|
| "Server Tick Batching" warnings — solved 29 Oct (two passes) | 1) Lone ~18 ms editor GC hitches with Netcode's 4-frame window. 2) Netcode's default of **1 tick per frame**: any frame longer than a tick (editor at ~60 FPS with vsync, the match load) merged ticks into one batch | Incremental GC; no HUD garbage; dev window 60 frames; **global `NetcodeConfig` (Setup step 1): up to 4 full ticks per frame** (batching only for a real overload); warning muted for the first 5 s of a match (load). Tested at 60 FPS: 1,795 ticks in 30 s, no batching after load, no warnings |
| Burst "not a known entry point" messages | Stale Burst cache after big code changes | Same: clear `Library/BurstCache` |
| Only tested with thin clients and one editor on Edgegap (29 Oct: 4 players, 170 ms, 2% loss, smooth; Edgegap RTT 40 ms, smooth) | — | A test with people, in client builds (editor stalls cause catch-up snaps) |
| Edgegap `0.1.0` has no idle stop | Built before `ServerLifecycle` | Stop deployments by hand in the dashboard; build and push `0.1.1` |
| MessagePipe without a DI container (`PresentationEvents`) | API not checked in an editor before | Watch for errors on first message |

## 6. Next steps (recommended order)

Next session: continue with this order (step 1 onward). Newest (29 Oct evening): first Edgegap deploy works (step 4, Phase 3); owner TODOs for the damage model and death break-up are in plan section 7d. The feel pass and style guide work of 28 Oct is done; its follow-ups (feel tuning, match-end, lobby and server list screens from Figma) are the backlog in plan section 7d.

1. **Phase 2 is done (29 Oct):** the 4-player network test (thin clients, see step 8 above), the edit-mode tests (63/63) and the tick-batching warnings (open-problems table). Left: a test with people.
2. **Phase 4: modular build.** Plan section 5, "Modular build: lego sockets":
   - done (not yet compiled): sockets, module visuals, the loadout on the ghost, each player's loadout at spawn;
   - done: PartForge Kit01 (neon) and Kit02 (dystopia, the owner's direction; default), 4 chassis + 23 modules each; art direction in `Tools/PartForge/ART_DIRECTION.md`; Setup step 5 imports the kits;
   - done (28 Oct, not yet in Unity): PartForge alignment check (`generate.py --audit`, 0 problems), review studio lighting and four-view sheets, baked texture sets (UVs, normal, occlusion, wear and grime) and 10 skins (`partforge/skins.py`; Unity: one URP Lit material per set and skin, `SkinLibrary` per kit, linked in `GameConfig`);
   - done (28 Oct, not compiled): runtime skin shader for A/B against the baked skins: `Art/Shaders/ChopperSkin.hlsl` (Shader Graph custom function; the graph is made by hand, see `Art/Shaders/README.md`), skin table texture from `SkinLibrary` looks (`SkinTable.Encode`, test `SkinTableTests`), one runtime material per texture set, `SkinPreview` component (Baked / Runtime, next skin, flip timer), skin id from `LoadoutChoice.SkinId` → `GoInGameRequest` → `RequestedLoadout` → `ChopperSkin` ghost field, `SkinApplySystem` (per-entity `_SkinIndex` override) and module visuals (property block);
   - decided (owner, 29 Oct): **runtime skins**. Baked skin maps are no longer exported (style `baked_skins` brings them back); kit models use the skin-shader material;
   - done (29 Oct, in the editor): Setup step 6 "Build chassis from kit": one ghost prefab per chassis (kit model, sockets from the markers, box collider), kit models on all 23 modules, rotor spin on rotor modules (`ModuleDefinition.VisualSpin`, odd sockets turn the other way), a socket with no slot of its own repeats the first module of its type (Lifter's tandem rotor); with no loadout choice the server spawns the default chassis's own prefab (`ChopperSpawner.DefaultChassisId`);
   - next for skins: the server checks skin unlocks (progression);
   - done (29 Oct): body colliders that follow the model: Setup step 6 cuts each kit body into 4 slices along its length and fits a box to each (thin masts and aerials trimmed, stub wings kept); they replace the single root box and bake to one compound collider;
   - next: socket scale (manifest `scale`, all 1.0 now);
   - done (29 Oct, in the editor): the **hangar** (`Assets/Scenes/Hangar.unity`, Setup step 7 "Build hangar"). Green wireframe chopper on a turntable (feature lines baked per kit mesh into `Data/Hangar/WireframeLibrary.asset`, dark fill, bloom); chassis tabs; slot list; click a slot = the camera and turntable blend to that socket (automatic pose: the socket's side, from above, or from below for the bottom half; per-socket poses with `HangarSettings.useSocketPoses`); module list with level locks, hover = stat preview (green better / red worse), weight bar; skin picker and "show paint" (real material with the runtime skin); launch saves the choice (PlayerPrefs, `LoadoutChoiceStore`), starts a local server + client (`Session/LocalSession.cs`) and loads TrainingRange. The client world reads the saved choice at start (`LoadoutChoiceSystem`), so starting TrainingRange directly also uses it. Tested: Gunship with air mines and the Desert skin spawned on server and client. Logic in `HangarModel` (tests: `HangarModelTests`); unlocks are all open until progression (`AllUnlocked`);
   - done (29 Oct): way back from the match: Escape menu → [ HANGAR ] (`PresentationContext.OpenHangar` → `LocalSession.Stop`, then the hangar loads); tested match → hangar → match twice;
   - done (29 Oct): muzzle at the barrel tip: `ModuleDefinition.Muzzle` (offset from the socket), measured from the kit model by Setup step 6; guns and pylons fire from socket + tip;
   - done (29 Oct): rotor speed follows the collective (+/-25 %, `ModuleVisualSystem`; remote choppers too, it is a ghost field);
   - done (29 Oct): handling pass: Scout fast and snappy, Lifter the heaviest (slowest turn and response, strongest climb), Assault between; module stats for top speed and boost power; hangar bars in real units (plan section 5, "Handling pass");
   - owner TODO (29 Oct): more part tweaks, part upgrade chains (trees per part line, part mastery), optional trim, and chassis variants per class with their own model base, handling and ability (e.g. a tandem and a single-rotor Lifter) — plan section 5, "Parts, upgrade chains and chassis variants";
   - done (29 Oct): music: `MusicLibrary` in `GameConfig` (Setup step 1): identity track "Heavy Riff 1" (the old main menu's music) in the hangar and menus; matches play a shuffled playlist of 12 tracks, metal from the music packs blended with the owner's dark synth picks (Sound/Music), cross-faded; music streams from disk. The bridge picks the set (`PresentationBridge.music`);
   - owner TODOs (29 Oct): rocket lead reticle always on with rockets selected; many rocket systems (slow heavy, fast light, aim-guided, smart, salvo) and a rich roster of guns and abilities (plan section 5, "Rockets and a rich weapon roster"); the original 2023 vision mapped onto v2 (plan section 0);
   - flight (29 Oct, owner request): faster descent, `CollectiveDown` 10 → 18 m/s² on all chassis (Ctrl / S);
   - next for the hangar: make it the first scene once the old menus are ported; module slide-in / scan animations and sounds; mouse drag to turn the chopper; tune focus poses per chassis; lighter bloom if the owner wants thinner lines; show it while matchmaking searches (plan section 4).
3. **Targeting, guided weapons and the dynamic HUD** (plan section 3b):
   - done (not yet compiled): heat-seeker locks, missile ghosts (AA and AG), flares, lock brackets and warnings;
   - done (not yet compiled): retro 80s HUD look (HUD to a render texture, full-screen CRT pass, `HudLookSettings`, gameplay glitches);
   - done (not yet compiled): options toggle for preset / reduce effects; phosphor persistence;
   - next for the look: tune it in play; per-element Shader Graph UI materials; same look for menus and hangar;
   - done (not yet compiled): cluster missiles, world markers, rocket lead indicator;
   - done (not yet compiled): swarm micro-missiles (Swarm Pod);
   - next: team colours on markers when TDM exists; objective markers (nuke, bases); the other weapons in the table (railgun, EMP, chain lightning...).
   It can run in parallel with step 2: the weapons are modules.
4. **Phase 3: services** (started 29 Oct; the project is linked to UGS "Airwing Nitro"; the owner has an Edgegap account):
   - done: the composition root runs: `RootLifetimeScope` prefab + `VContainerSettings` in the preloaded assets (Setup step 1, `RootScopeSetup`); before this, sign-in never ran;
   - done: Steamworks.NET 2025.165.0 (package), `steam_appid.txt` = 480 (Spacewar test ID until the owner's App ID). Tested: Steam gives a ticket; Unity refuses it ("id provider not found", no Steam provider in the dashboard yet) and the game signs in anonymously with the Steam name;
   - owner: when the real App ID exists, put it in `steam_appid.txt` and add the Steam provider in the Unity Dashboard (App ID + Web API publisher key, identity `unityauthenticationservice`);
   - owner TODO: Relay friend matches as a step before Edgegap (plan section 4);
   - done (29 Oct): **Linux dedicated server build**: Setup window → "Build Linux server (Edgegap)" (`Editor/Setup/ServerBuild.cs`): switches the editor to Linux Server, waits for the script reload (`UNITY_SERVER`), builds TrainingRange only to `Builds/LinuxServer` (235 MB), copies `Tools/Edgegap/Dockerfile` + `.dockerignore`, switches back (about 20 min the first time, shader compile). The server bootstrap makes only a server world listening on `-port` (default 7979). Gotcha: the product name "Airwing: Nitro" has a ':' that Entities cannot use in a folder name; the build removes it while building (decide a ':'-free product name before client builds);
   - done (29 Oct): **tested the Linux server in WSL** (Ubuntu): it loads the match and listens on UDP 7979; the editor as a client (Multiplayer → Play Mode Tools: Play Type Client, address = WSL IP from `hostname -I`, port 7979) connects, gets NetworkId 1, the server spawns its chopper (hangar loadout and skin), 8 ghosts, RTT ~10 ms. Server builds strip the presentation objects (camera, HUD, options, bridge: `ServerSceneStrip`; build 235 → 165 MB) and skip Steam and player sign-in (`RootLifetimeScope`). Run it: `wsl -d Ubuntu -- bash -lc 'cd /mnt/d/.../Builds/LinuxServer && ./AirwingServer.x86_64 -batchmode -nographics -logFile /tmp/airwing.log -port 7979'`. Seen once, not repeated: the first connect to a just-started server dropped right after the join request; watch for it. The release server log is buffered (read it after exit);
   - done (29 Oct): Docker Desktop (29.9) installed; the image builds from `Builds/LinuxServer` (`airwing-server:0.1.0`, 110 MB) and runs in Docker: the server loads the match and listens on UDP 7979 (about 220 MB RAM, 7% CPU idle). `Tools/Edgegap/push.ps1 -Registry registry.edgegap.com/<org>/<app> -Version <v>` builds and pushes;
   - done (29 Oct, not yet compiled): idle stop for dedicated servers (`Session/ServerLifecycle.cs`): no player in 300 s after start, or nobody for 120 s after the last player left → asks Edgegap to end the deployment (`ARBITRIUM_DELETE_URL` + `ARBITRIUM_DELETE_TOKEN`), then quits. `-idleStart` / `-idleEmpty` override (0 = never). Port: `-port`, else Edgegap's `ARBITRIUM_PORT_GAMEPORT_INTERNAL`, else 7979 (name the Edgegap port "gameport"). Tests: `ServerIdlePolicyTests`. Needs a new server build to take effect; check: `docker run --rm airwing-server:<v> -idleStart 20` exits after about 20 s;
   - done (29 Oct): **first Edgegap deploy, tested**: image `registry.edgegap.com/superhotdancergames-wffvi2kvblrl/airwing-server:0.1.0` (Docker login on the owner's PC), app `airwing-server`, version `0.1.0` (port `gameport` 7979/UDP, 0.5 vCPU, 1 GB, Never Restart). The editor joined as a client (Play Mode Tools → Client, deploy IP + external port): spawn, combat, RTT ~40 ms, 0 packet loss, no snapshot gaps, input reaches the server ~2 ticks early. Over 30 s of scripted flight: no hitch except after a 1.2 s editor stall (then 11–15 m catch-up snaps for 0.3 s); corrections p99 1.4 m/frame (local server: 0.85). Fixed on the way: client receive queue 64 → 256 (overflowed while the client loaded), and a dark square on the HUD at low hull (hull arc took the readout's "low" class with its background);
   - next: build and push `0.1.1` (idle stop), duplicate the app version with the new tag; then matchmaking → Edgegap (Cloud Code allocator);
   - Steamworks.NET and the Dashboard Steam provider;
   - Multiplayer Services sessions with a Cloud Code allocator for Edgegap;
   - a dedicated server build;
   - party and rejoin.
5. **Phase 6: modes:** TDM, then Capture the Nuke, then bots. Owner TODO (29 Oct): bot AI with path finding and a needs-based (utility) decision system, for air and land units — plan section 7a.
6. **Phase 5: progression:** Cloud Code grants, Cloud Save, unlocks, leaderboards and Steam achievements.
7. **Any time:**
   - port the old menus to UI Toolkit and delete `Assets/Scripts` (the options overlay `UI/Options` is the first piece; reuse its style);
   - export the `.blend` models to `.fbx`;
   - delete or rebuild the old scenes.
   - owner TODO (29 Oct): a city building generator like PartForge (Blender, geometry nodes, styles, export + Setup step) — plan section 7a2.

## 7. Owner preferences

- The world is a **gritty, kinetic cyberpunk**: keep the old game's feel wherever possible. Examples: the HUD that moves with the chopper, the pitched explosion gun sound, lasers as a late unlock.
- The hangar and UI are green cyberpunk, with a 3D wireframe chopper. The camera focuses on each socket.
- HUD: retro 80s look — CRT / vector-display shader or post-processing over the HUD (plan section 3b).
- Weapons: heat-seeking missiles (air and ground), cluster missiles and other cool cyberpunk weapons; a dynamic HUD overlay with targeting (plan section 3b).
- Choppers will become fully modular ("lego" modules on chassis sockets). The current chopper is a placeholder.
  - Target modularity (owner reference): https://www.fab.com/listings/a090fe12-34f9-4eab-9ca8-5f979a29e01f
  - Parts are generated procedurally in Blender (`Tools/PartForge`, see its README), not Asset Forge (its blocks look too childish). Kits in `Assets/Chopper/Art/Kits`. To tweak: open `Tools/PartForge/blend/Kit02.blend` in Blender 5.x, allow scripts, N → PartForge. Plan section 5, "Part kits and authoring pipeline".
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
| Hangar | `Presentation/Hangar/*` (`HangarModel`, `HangarStage`, `WireframeMesh`, `LoadoutChoiceStore` + `LoadoutChoiceSystem`), `UI/Hangar/*`, `Simulation/Session/LocalSession.cs`, setup `Editor/Setup/HangarSetup.cs`, data `Data/Hangar` |
| Tests | `Tests/EditMode/*` (HUD models, HUD look, markers, cluster, health, loadout rules, loadout code, guidance, audio, hangar model, wireframe, saved choice) |
