# Chopper game — handoff (updated 7 Nov 2026, input actions, scoreboard, impact sounds)

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
| Local path | `D:\Uni\Unity\AirwingNitrov2\chopper-game` (Windows; MCP for Unity connected on 6 Nov) |
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
  - Weapon limits (6 Nov): see section 4a.
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

## 4a. Newest work (6 Nov): weapon limits

Commits `a10070b3` (limits, chassis turn weight) and `a29da0cc` (reload bars). Compiled, 126 of 126 edit-mode tests pass. Maths: `Simulation/Combat/WeaponLimits.cs` (`GunHeat`, `Magazine`). Values: the weapon definitions in `Data/Loadout`.

| Weapon | Limit |
|---|---|
| Minigun | Heat: 6 s of fire to overheat, 4 s to cool |
| Autocannon | Heat: 5 s, 4.5 s |
| Pulse Laser | Heat: 4 s, 3 s |
| Rocket Pod | Magazine: 14 rounds, 6 s refill |
| Heat Seeker AA | Magazine: 4 rounds, 12 s refill |
| Heat Seeker AG | Magazine: 2 rounds, 14 s refill |
| Cluster Missile | Magazine: 2 rounds, 16 s refill |
| Swarm Pod | Magazine: 2 salvos, 18 s refill |
| Flares | Magazine: 3 drops, 18 s refill; cooldown 3 s (was 8 s) |

- **Heat:** each shot adds heat. Cooling starts 0.2 s after the next shot was due, so steady fire never cools. At full heat the gun locks out until it cools to 40 %.
- **Magazine:** stores rounds used, so the default state (0) is full and a new chopper needs no setup. It refills fully when the refill time has passed since the last shot; each shot restarts the time. A salvo is one round. Size 0 = no limit. Reload must be longer than the fire interval (Validate checks it).
- **Network:** new unquantized ghost fields on `HitscanWeaponState`, `PylonWeaponState` and `CountermeasureState`. **This changes the netcode protocol: rebuild and push the Edgegap server (`0.1.2` is older than this) and change the allocator's `Version`, or online clients cannot connect.** The 5 Nov damage model also changed ghost and RPC data. State (6 Nov): image `airwing-server:0.1.3` is built and pushed (311 MB build; it ran in Docker locally, loaded a map and stopped itself when idle; not joined by a client). Edgegap versions are now `0.1.2` and `0.1.3` (limit of 2 on the account; `0.1.1` was deleted, its image stays in the registry). `EdgegapAllocator.cs` says `0.1.3`, deployed to development by the owner. Not done: deploy to production. Production may still start `0.1.2` (not checked), so keep `0.1.2` until then. `release.ps1` now drops null and read-only fields when it copies a version.
- **Server size (6 Nov):** server baking skips `Unity.Entities.Graphics` (no render data; `NetcodeSettingsSetup.EnsureServerBaking`, Validate checks it), and the image base is `debian:bookworm-slim`. Build 311 → 205 MB, image 611 → 434 MB. Tested from local Docker: `LocalSession.Join` from the hangar to Map_City04, spawn, gun shots counted, a dive stopped on the ground with crash damage from the server. Measured one sample with 1 player: 247 MiB RAM, 53 % of a CPU core (Edgegap version has 0.5 vCPU: check this online). This smaller build is image tag `0.1.4`. **Edgegap version `0.1.3` runs image tag `0.1.4`** (`release.ps1 -InPlace 0.1.3`: the allocator was already deployed to development with `0.1.3`, so no new deploy; to undo, PATCH `docker_tag` back to `0.1.3`). Image tag `0.1.3` is the 311 MB build. The owner tested it on 6 Nov and reports that the server worked. A 38.5 MB archive is left in StreamingAssets, contents not checked (our traffic and catalog references are likely).
- **HUD:** heat arc under the ring (yellow, red with OVERHEAT at lockout); round count under the pylon name (red RELOAD when empty); a refill bar under the round count and under the flare count, hidden when full. Written in `TargetingHudSystem`, shown through `ChopperHudModel.UpdateWeaponLimits`.
- **Cockpit Systems page:** rounds, flares and gun heat as text, no refill bar (a bar would push the rows out of the visible band of wide screens). Not yet viewed close up.
- **Tested in play (Map_City04, scripted input, client and server compared):** minigun overheat, lockout and resume; cluster missile blocked at 2 rounds, then refilled; flares blocked at 3 drops with test values (1 s cooldown, 6 s refill), then refilled; respawn gives full magazines; no Burst errors.
- **Not tested:** Rocket Pod, Swarm Pod, Autocannon, Pulse Laser and Heat Seekers fired in play; a reload bar filling during real firing (checked by a screenshot with state set by hand); online play. Balance has not been judged. The owner still has to judge the turn weight too (section 6, step 7, "Turn weight").
- **Later:** manual reload key, overheat and reload sounds, refill bar on the cockpit page.
- **How to test without a person:** in the client world set `ChopperInputSystem.Enabled = false`, then write `ChopperInput` to the local chopper each editor update (`execute_code` through MCP). This takes the input from anyone flying at that moment; re-enable it afterwards. Writes to config last only until the next spawn.
- **Screenshots in play:** use MCP `manage_camera`. `ScreenCapture.CaptureScreenshot` from an editor update can fail ("exceeds the active Render Target").

## 4b. Earlier work (27 Oct, not yet compiled at the time)

- Targeting, heat-seeking missiles (missile ghosts with predicted spawn), flares, pylon weapon cycling, lock and threat HUD, tones. See plan section 3b, "Built".
- After pulling: Chopper → Setup → **Do all** (makes the missile prefab, weapon assets, effect/sound slots, re-bakes the default loadout, makes targets pre-spawned ghosts), then Validate.
- Controls: RMB / E / LB fire the selected pylon; wheel / X / D-pad right switch pylon (Tab was moved to the scoreboard on 7 Nov); Space / triggers drop flares.
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
  - Cockpit is the default view (saved option `Chopper.Camera.DefaultView`, view name, set in Options → START VIEW (30 Oct, tested); C / gamepad Y only switches for the session). Hold Alt / R3 on a mounted view to look around (±130° yaw, −50..+65° pitch), release snaps back; mouse and pitch/roll axes are neutral meanwhile (`PlayerInputSettings.HeadLook`).
  - PartForge makes one interior per chassis (`parts.cockpit`, preview: `Tools/PartForge/cockpit_preview.py`): Gunship and Assault a minimal military cockpit (low faceted shell and instrument panel facing the eye, glare shield at about −18°, two square displays, side-stick, pedestal, thin frame, armoured seat); Scout the same with an open bubble (no frame in front, about −25°: the widest view, its edge); Lifter the busy 80s cabin dash. Socket `SOCKET_Eye_0` = the pilot's eye. `--audit` checks the cockpits too.
  - Cockpits use the kit palette maps directly (UV0 = palette cells, texture set "Palette", Unity material `Kit02_Palette`): baked sets cannot hold millimetre screen lines.
  - Setup step 6 links each kit cockpit and eye to its chassis (`ChassisDefinition.Cockpit` / `CockpitEye`). `CockpitVisualSystem` shows it for your own chopper only, in mounted views; mounted camera views sit at the chassis eye (`CameraView.FixedEye` turns that off). In a cockpit view your own hull (`DisableRendering` on its entities) and module art are not drawn: from inside, the hull's outside details (nuts, slits, plates) floated in the view; the pilot sees out through a "camera feed". `CameraView.ShowOwnChopper` keeps them (for a later gunner view). Tested 30 Oct in play: Gunship, Scout, Lifter; chase view unchanged.
  - Also changed for the cockpit view: the Gunship/Assault frame between the canopies is lower; the Lifter's glazing loft has no back cap (`Builder.loft(open_back=True)`). Both were made before the hide rule and are now optional.
  - Camera-feed boot-up (30 Oct, tested in play): in a cockpit view the world is a camera feed. On spawn it is full static, then rolls, jitters and clears (`HudLookReactions.FeedBootHold` 0.45 s + `FeedBootSeconds` 1.6 s); hits bring noise back (`FeedGlitch` x the HUD glitch). One extra term in the HUD look pass (`HudLookModel.Feed`, `HudLook.shader` `WorldFeed`); it reads depth only while the feed is on, and pixels nearer than `HudLookSettings.feedCockpitDepth` (4 m) are the cockpit and stay clean. Off with the Clean preset; weak with reduce effects (no snow).
  - Camera feel per view (30 Oct, owner: the cockpit camera was too wild): each `CameraView` has its own feel block (`Inertia` lag, `BoostFieldOfView`, `Rumble`, `Shake`, `Effects` = speed/boost post effects and speed lines); `CameraSettings` keeps only the shared shapes (springs, frequencies, limits). Blends mix the two feels. Cockpit: no lag, +4° boost FOV, rumble 0.35, shake 0.45, effects 0.3, noise 0.06° at 3 Hz, max FOV 73. Chase unchanged. Not yet tuned in play by the owner.
  - Live cockpit screens (30 Oct, tested in play on the Lifter): PartForge gives each display whose placeholder has a page (`palette.SCREEN_OF`: horizon -> Flight, radar/map -> Tactical, bars/text -> Systems; the "rear" strip stays baked) a screen quad with material `<kit>_Screen` and UVs into a 3-page atlas (a wide display shows the middle band of its page). Unity: `CockpitScreensView` (UI/Cockpit, "Cockpit Screens" object from the scene setup step) draws `CockpitScreens.uxml` through its own panel settings into `CockpitScreens.renderTexture` (1536 x 512); `Kit02_Screen` (shader `Chopper/CockpitScreen`: self-lit, scanlines, writes depth) reads it. Bound to `ChopperHudModel` (new: `PitchDegrees`, `HeadingDegrees`) and `WorldMarkerModel` (new: `BearingDegrees`), the HUD's data path. Pages: Flight (horizon `AttitudeIndicator`, heading, speed, altitude), Tactical (`RadarScope` 500 m, lock state, RWR / missile), Systems (`SegmentBar` hull and boost, weapon, flares, warnings). Drawn only in a cockpit view.
  - Fixed: the PartForge export deleted the palette maps' `.meta` files each run (the texture writer's old-map cleanup); it now keeps them.
  - Not yet: a rear / weapon camera feed on the Lifter's rear strip.
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
9. Run edit-mode tests (Window → General → Test Runner): 126 tests (6 Nov).
10. Console (29 Oct): 0 errors, 0 warnings after a clean compile and in play. Legacy assets with deleted scripts moved out of `Resources` to `Assets/Legacy` (builds packed them); the old Cinemachine camera scripts joined the retired legacy code in `Assets/Scripts/_LegacyNGO~`; TextMesh Pro font metas upgraded; Shapes patched (UAC0005). Note: the MCP console reader shows Burst `Debug.Log` lines ("[Client] Connected") as errors; Unity counts them as logs.
11. Hangar: Chopper → Setup → **7. Build hangar** (or Do all) after new kit models; "Open hangar scene", press Play, pick a loadout, LAUNCH.
12. Restart the editor once after pulling: incremental GC (Player settings) starts with the editor. If Burst "not a known entry point" messages come back: clear `Library/BurstCache`.

## 4c. Newest work (7 Nov, written without the editor, not compiled)

- **Scene flow** (`Presentation/Flow/SceneFlow.cs`, run by `App/SceneFlowEntryPoint.cs` in the root scope, players' builds only):
  - Scene changes go through `PresentationContext.LoadScene`. Flow: the screen breaks into blocks (`UI/Transitions/DigitalWipe.cs`), a callback runs (start or stop the local session), the scene loads async, the blocks clear.
  - A match scene stays covered until your chopper spawns (12 s at most).
  - The game's first scene also clears in from blocks.
  - World sounds (Effects and Ambient mixer channels) fade with the cover (`MixerVolumes.WorldFade`).
  - Music lives on the flow (`MusicPlayer`), so it plays on through the load and cross-fades into the new scene's set. The same playlist again keeps playing.
  - Settings: `Data/UI/SceneTransitionSettings.asset` (timing, colours, sounds). Its own panel is `UI/Settings/TransitionPanelSettings.asset` (sort order 1000). It is linked on `App/RootLifetimeScope.prefab`, not on the GameConfig.
  - Without the root scope (tests), scenes load at once as before.
- **Music level** (`MusicLibrary`):
  - Menus 1.0.
  - In a match while you fly 0.4, so the rotors and guns come through.
  - Before spawn, while you wait to respawn and on the results 0.8.
  - Change speed 4 s. This is on top of the player's music volume.
- **Field repair:**
  - Hold **R** or gamepad **X** (`Flight/Repair` action since 7 Nov, see 4e). The hull mends from empty to full in 10 s.
  - Any damage (shot, wall, landing, bounds) locks it for 5 s, also when it was not running; each new hit starts the lock again, damage over time too. When the lock ends, a key still held starts the repair. The HUD shows "REPAIR LOCKED 4.2" and a yellow bar that fills.
  - Server: `Simulation/Combat/Repair.cs` (rules, `ChopperRepair` ghost state, `RepairConfig`), `RepairSystem`. `ApplyDamageSystem` interrupts the repair.
  - Per chassis: `RepairTuning` on `ChassisDefinition` (pilots will scale it).
  - Client:
    - The HUD line above the hull: hint below 75%, "REPAIRING 63%", "REPAIR LOCKED 2.4". The hull arc and readout run cyan while repairing.
    - Blue-white welding sparks that everyone sees (`DamageEffectsSystem`, `CombatEffectsSettings` repair spark rate and colour).
    - Own sounds: a servo hum, a chime when full, a cut-off on a hit (`SoundLibrary` repair cues).
  - Tests: `RepairTests`.
  - **New ghost fields and a new input field: rebuild and push the Edgegap server and change the allocator's `Version`.** If the chopper ignores input after the compile, delete `Library/BurstCache` (see the Burst note above).
- **Pilots:** design only, plan section 5b (call signs, select screen, abilities).
- Editor steps:
  1. Compile.
  2. Chopper → Setup → **1** (makes the transition settings, panel and sounds, links them on the root prefab, adds the repair cues), then Validate.
  3. Re-bake the chopper prefab (step 3 or Do all), because the ghost has new components.
  4. Play from the Hangar:
     - LAUNCH: check the blocks, the music cross-fade, the map name and the reveal when the chopper spawns.
     - Escape → hangar: the same, the other way.
     - Take damage and hold R: check the hint, the progress, the sparks and the sounds. Get shot while repairing: check the lock.
  5. Tune `SceneTransitionSettings` and the `MusicLibrary` levels.

## 4d. Pilots, steps 1 to 5 (7 Nov, written without the editor, not compiled)

- See plan section 5b, "Built".
  - There are 10 call-sign pilots. Each has a passive with a cost and an active ability: **F or D-pad up** (step 4).
  - The pilot id travels with the loadout.
  - Damage over time: VIPER corrodes, PYRE burns.
  - The hangar has a fighting-game style select screen (PILOT card, then the full-screen picker).
  - Step 5 placeholders: drawn helmet portraits, and generated voice lines (`Tools/Voice/make_pilot_voices.sh`; the announcer says the call sign, the pilot calls the ability). Call signs show in the kill feed and under your score.
- **Protocol change** (new ghost fields `ChopperLoadout.PilotId`, `StatusLook` and `AbilityState`, new input and RPC fields): rebuild and push the Edgegap server and change the allocator's `Version`.
- Editor steps:
  1. Compile.
  2. Setup step 1 (makes `Data/Loadout/Pilots/Pilot_*.asset` and links them in the catalog), then Validate.
  3. Reimport the chopper prefabs (new components).
  4. Hangar → PILOT card → check the slam, the chips and LOCK IN → TRAINING.
  5. Check a pilot in play:
     - APOTHECARY: repair in 6 s.
     - VIPER or PYRE: green sparks or flames on a target, and the damage keeps ticking.
  6. Abilities: press F with each pilot. Check the HUD line, the cooldown, the look and the effect:
     - BULWARK: hits do nothing while the shield holds.
     - HOTWIRE: boost does not drain.
     - HEX near a target: the enemy's lock drops.
     - OVERWATCH: far enemies show in cyan.
  7. If input breaks after the compile, delete `Library/BurstCache` (new `ChopperInput` field).
  8. Run `PilotTests` and `AbilityTests`.

## 4e. Input actions, scoreboard, impact sounds (7 Nov, written without the editor, not compiled)

- **Input actions** (`Simulation/Input/PlayerControls.inputactions`, Flight map): new actions `Repair` (hold R / gamepad X), `Ability` (F / D-pad up), `CyclePylon` (mouse wheel either way, **X**, D-pad right) and `Scoreboard` (hold **Tab** / gamepad **View**). `ChopperInputSystem` no longer reads the devices. Tab used to switch pylons: it is the scoreboard now, X took its place. The generated `PlayerControls.cs` was edited by hand to match; Unity makes it again on import (same result).
- **Scoreboard** (hold Tab / View; it also shows on the results under the banner, which moves up):
  - Columns: place (ties share it), pilot (`VIPER 3`, or `PILOT 3`), chopper (chassis name), kills, deaths. Your row is yellow. Sorted by kills, then fewer deaths, then id.
  - Model: `MatchHudModel.Scoreboard` / `ScoreboardVisible` / `ScoreboardTitle` (tests in `MatchHudModelTests`). `PilotNamesSystem` now also sends each chassis name. The key is read in `PresentationContext.Tick` (not while a menu is open).
  - View: `UI/Hud/ScoreboardView.cs` (pooled rows), the `scoreboard` block in `ChopperHud.uxml`, styles in `ChopperHud.uss`. No ping column yet (the server does not send other players' ping).
  - No protocol change: it uses the score RPCs that exist.
- **Impact sounds by surface** (no protocol change):
  - Surfaces: metal, concrete, ground, water (`Simulation/Visuals/Surface.cs`). `Surfaces.Classify`: a `SurfaceAuthoring` on the collider wins; else any moving body (chopper, target) = metal, the "Water" layer = water, mesh or terrain collider = ground (city terrain chunks), else concrete (building boxes). Setup step 8 marks industrial and shanty boxes as metal.
  - Bullet hits: `GunEffectsSystem` plays the surface's hit cue where each bullet lands (a ping on a chopper, chips off concrete, a thud in dirt, a splash).
  - Crashes: new `Presentation/Audio/CrashAudioSystem.cs`. For the server's crash and landing hits (all choppers, heard by everyone near) it casts a short ray into what was hit and plays that surface's crash cue, louder for harder hits (`CrashAudioSystem.Volume`, test `ImpactAudioTests`). Your own crash no longer also plays the generic hit sound.
  - Sounds: placeholders made from noise by `Tools/Sound/make_impact_sounds.py` → `Assets/Sound/Impacts/*.ogg` (22 files). Setup step 1 makes 8 cues (`Data/Audio/Cue_MetalHit` … `Cue_WaterCrash`) on `SoundLibrary` → Surfaces. Swap in real recordings on the cues later.
- Editor steps:
  1. Compile. If Unity asks, let it make `PlayerControls.cs` again from the asset.
  2. Setup step 1 (makes the 8 surface cues), then Validate.
  3. Setup step 8 for each city (marks the metal boxes), then step 10.
  4. Play a city map:
     - Hold Tab: the scoreboard shows; let go: it hides. Check it at the end of a match too.
     - Wheel / X / D-pad right switch pylons; R / X repair; F / D-pad up ability.
     - Shoot the ground, a building, a tank farm and a target: different hit sounds.
     - Fly into a wall and land hard: a crash sound.
  5. Run `MatchHudModelTests` and `ImpactAudioTests`.
  6. Tune the cue volumes in `Data/Audio`.

## 4f. Audio balance, tuning buttons, cockpit boot-up (7 Nov, compiled and run in the editor)

- Compile errors from 4c to 4e are fixed; Setup step 1 is done and its assets are committed. Steps 8 and 10 are not run yet.
- **Audio balance** (measured; the owner has not yet judged the result by ear):
  - The rotor clip was at -33 dB and the city clip at -21 dB. `Assets/Sound/Rotor_Interior_Loop.wav` is the rotor clip raised 17 dB, with a seamless loop. Rotor cues use it.
  - New `Cue_CityAmbience` (volume 0.18, Ambient channel) loops on `Map_*` scenes.
  - `MusicLibrary`: menu 0.5, combat 0.22, idle 0.4, on top of the player's music volume.
- **Cockpit muffling:** each cue has a `Muffle` kind. `Outside` (other choppers, guns, bullet hits, flares, city): `SoundLibrary` → `cockpitCutoff` 800 Hz. `Cabin` (own rotor, boost, hull hits, crashes, repair hum): `cabinCutoff` 1500 Hz. `chaseCutoff` 22000 Hz = not muffled in a chase view. Explosions on effect prefabs have their own AudioSources and are not muffled yet. Rotor pitch range is now 0.92 to 1.06 (`ChopperAudioSystem`); it was 0.9 to 1.2. Volumes: rotor 0.6, boost loop 0.5, boost kick 0.6.
- **Transition tuning:** select `Data/UI/SceneTransitionSettings.asset`; in play mode press **Preview transition**. Changes show at once.
- **Cockpit boot-up** (`HudLook.shader`, `HudLookSettings` → Reactions; **Replay cockpit boot-up** button): big pixels that resolve in patches, few colours, dead blocks, and a repeating honeycomb in the screen corners. `HexGridRest` above 0 keeps the honeycomb on after the boot-up. Hits still give the analog noise.
- **Bright flash in the cockpit after boost (owner, 7 Nov): cause found on a paused frame, fixed.** With a server world in the same process (training, local sessions, the editor), the server's copy of your chopper had real Lights too. It is behind the predicted chopper, more so at boost speed, so its nose lights (green, red, white spot at 500) shone inside the cockpit. `Presentation/Visuals/ServerLightsOffSystem.cs` turns off every Light in the server world. A dedicated-server client never had the flash. This also stops the city's lights counting twice against the Forward+ limit (259 were doubled to 518), so more lamps may show now.

- **Input prompts:** `Presentation/Hud/InputPrompts.cs` shows the keys of the device used last (saved), with the connected pad's own button names; flight keys come from the input actions. The HUD gets them through `ChopperHudModel.SetPromptKeys`. In UXML, a label with the class `input-prompt` holds a template such as "{Back} = BACK" (`UI/Theme/InputPromptLabels.cs`). Not yet tested with a real pad.

- **Sphere HUD:** `SphereAngle` on the look presets (`HudLookSettings`, 42 degrees; tune it live in play mode) bends the HUD like the inside of a sphere round the pilot. Text tilts more the further it is from the screen's horizontal centre line, so the side text stands away from it on purpose: speed and altitude above, hull below, weapon above, flares and ability below. It replaces the barrel curvature. `HudSphere.ToHud` (C#) and `HudLook.shader` hold the same formula: change them together. Anything placed on a world point goes through `HudLook.ToHud` (reticle, lock, lead, markers). The panel's corners go past the screen, so `ChopperHudView` moves the scores and kill feed in by `HudSphere.Inset`; other text must stay out of the corners too. Checked in play mode: probes placed with `ToHud` landed on their expected screen pixels. The Clean preset stays flat.

- **Horizon on the HUD:** `ChopperHud.uxml` uses the cockpit's `AttitudeIndicator`, clipped to the ring; colours are in USS (`.hud-attitude`).
- **No boost while repairing (owner, 7 Nov):** `Repair.BlocksBoost`, read by `ChopperFlightSystem`. A boost in progress stops, also with an endless-boost ability; the tank still refills. The HUD shows the boost arc as not ready. Only the server writes the repair phase, so online the client's boost stops a moment late and is corrected. Rules tested; not yet tried in flight.

## 5. Open problems

| Problem | Likely cause | Next action |
|---|---|---|
| "Server Tick Batching" warnings — solved 29 Oct (two passes) | 1) Lone ~18 ms editor GC hitches with Netcode's 4-frame window. 2) Netcode's default of **1 tick per frame**: any frame longer than a tick (editor at ~60 FPS with vsync, the match load) merged ticks into one batch | Incremental GC; no HUD garbage; dev window 60 frames; **global `NetcodeConfig` (Setup step 1): up to 4 full ticks per frame** (batching only for a real overload); warning muted for the first 5 s of a match (load). Tested at 60 FPS: 1,795 ticks in 30 s, no batching after load, no warnings |
| Burst "not a known entry point" messages | Stale Burst cache after big code changes | Same: clear `Library/BurstCache` |
| Only tested with thin clients and one editor on Edgegap (29 Oct: 4 players, 170 ms, 2% loss, smooth; Edgegap RTT 40 ms, smooth) | — | A test with people, in client builds (editor stalls cause catch-up snaps) |
| Edgegap `0.1.0` has no idle stop | Built before `ServerLifecycle` | Stop deployments by hand in the dashboard; build and push `0.1.1` |
| MessagePipe without a DI container (`PresentationEvents`) | API not checked in an editor before | Watch for errors on first message |
| URP render errors in the console, seen once on 6 Nov after a recompile: NullReferenceException in `ReflectionProbeManager.UpdateGpuData`, InvalidOperationException for `ZBinningJob` | Unknown. Stack is editor view rendering, not game code | Did not return in the next play run. If they return, note what was open and running |
| Edgegap server is older than the current protocol | Ghost and RPC data changed (5 and 6 Nov) | Rebuild, push, new Edgegap version, change the allocator's `Version` before any online test |

## 6. Next steps (recommended order)

**Next task: first real playtest** (recommended 6 Nov, owner agreed): 2–4 people on client builds, on a city map, through Edgegap.
- **Why first:** network tests so far used thin clients and one editor. It answers weapon balance and magazine sizes, turn weight per chassis, hits on other players, and missiles, flares and locks against people. It also checks server load: one local reading was 53 % of a CPU core with 1 player, and the Edgegap version has 0.5 vCPU.
- **Needs:**
  1. A Windows client build. It is the first with the current maps, so it may show new problems.
  2. The allocator deployed to production, or test on development. Clients must be built from current code.
  3. A server log of tick time and CPU. First check whether `SessionDiagnosticsSystem` already gives this.
  4. A short list to judge: balance, feel, hitches, map flow.
- **After it:** bots if the playtest was fun (they also make solo testing easier), or Capture the Nuke for the signature mode sooner: the owner decides. Visual polish and the hangar callouts wait.

Then continue with this order (step 1 onward). Older note (29 Oct evening): first Edgegap deploy works (step 4, Phase 3); owner TODOs for the damage model and death break-up are in plan section 7d. The feel pass and style guide work of 28 Oct is done; its follow-ups (feel tuning, match-end, lobby and server list screens from Figma) are the backlog in plan section 7d.

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
   - owner TODO (29 Oct): **damage model**. **Written 5 Nov, compiled and played 6 Nov** (plan section 7d backlog, "Damage model"):
     - Every hit now carries a point, a surface normal and a kind (`DamageEvent.Point/Normal/Kind`, `DamageKind`: bullet, explosion, impact, landing, bounds). Guns, rockets, missiles and splash fill them.
     - Crash damage from the impact speed at the contact: `CrashSensorSystem` keeps the velocity just before the physics step; `EnvironmentDamageSystem` takes the change the step made (impact speed, its direction = contact normal). Contact from below = landing (own safe speed). Damage = max health x ((speed - safe) / (lethal - safe))^curve. Defaults (`CrashTuning` on each `Chassis_*.asset`): safe impact 5 m/s, safe landing 7 m/s, lethal 45 m/s, curve 1.5 (15 m/s wall hit = 12 %, 30 m/s = 49 %, a 21 m/s dive into the ground = 22 %). Ramming: the chopper that closed in faster gets the kill credit. No collision events needed.
     - Server sends `ChopperHitRpc` to every client (strongest hit with a point, at most one per victim per tick; point in the victim's own space).
     - Client `DamageEffectsSystem`: sparks along the normal at each hit; crash and hard-landing dust in the map's colour; the last 3 hit points smoke below 70 % health (grey, black below 40 %) and burn below 20 %. Smoke uses the replicated health, so spectators and late joiners see it. Own chopper in the cockpit view: nothing within 2.5 m of the eye. Settings: `CombatEffectsSettings` → Damage; materials `Data/Effects/DamageSparks.mat` and `DamageSmoke.mat` (step 1). Tests: `DamageModelTests`.
     - Editor steps: compile; press **1. Create / repair config** (makes the two materials); Validate; play TrainingRange and City04: shoot a target, crash into a wall, dive into the ground, fly at low health. Tune `CrashTuning` per chassis and the Damage numbers.
     - Camera shake matches the hit (`CameraSettings.HitTrauma`, "Shake (hits taken)"): guns by damage (40 = full), blasts x1.3, crashes by impact speed (30 m/s = full), hard landings x0.8, out of bounds none. `DamageTakenRpc` now carries the kind and impact speed.
     - Tested 6 Nov (Burst on, about 93 FPS in the editor): 25 m/s drop = 13.2 of 80, shake 0.47; bullet 20 damage shake 0.47; blast 0.63; 30 m/s impact 0.99. After these changes delete Library/BurstCache with Unity closed: the stale cache gave NullReferenceException in EnvironmentDamageSystem and ApplyDamageSystem. Real weapon fire not tested. 6 Nov: crash damage counts one hit over 0.1 s (`CrashDamage.Track`): a 25 m/s drop now does 18.1 of 80 every time. Next: rotor strike on walls (spin and damage from angular speed). Crash sounds and hit sounds by surface: built 7 Nov (4e).
   - owner TODO (4 Nov): **rotor downwash**: dust, dirt, grass, snow blown up from the ground when a chopper is low with rotors running (plan section 7c2). **First version written 4 Nov, not yet run**: `Presentation/Effects/DownwashEffectsSystem.cs` (client; ray down from each chopper, dust ring made in code, stronger and tighter when lower, small camera shake for the own chopper); settings in `CombatEffectsSettings` (Rotor downwash); material `Data/Effects/DownwashDust.mat` made by step 1. Editor steps: press '1. Create / repair config', fly low over ground with colliders, tune the numbers. Next: surface type (water spray, snow, grass), ground decal;
   - owner TODOs (29 Oct): rocket lead reticle always on with rockets selected; many rocket systems (slow heavy, fast light, aim-guided, smart, salvo) and a rich roster of guns and abilities (plan section 5, "Rockets and a rich weapon roster"); the original 2023 vision mapped onto v2 (plan section 0);
   - flight (29 Oct, owner request): faster descent, `CollectiveDown` 10 → 18 m/s² on all chassis (Ctrl / S);
   - done (3 Nov, **not yet compiled**): **new hangar layout** (plan section 5, "New layout"): no tabs; callout cards linked to the sockets; slot tiles along the bottom; chassis cards; stats float on the right (big numbers + bars); slot open = "SELECT <SLOT>", module tiles and a detail block; the painted chopper always shows with a faint wireframe over it (no paint toggle), less bloom; **each chassis keeps its build across sessions**; **named builds (presets)** per chassis (save, pick, rename, delete). Editor check: open the Hangar scene, play, look at the callouts and the tiles at 1920x1080 and a smaller size; run the edit-mode tests;
   - done (3 Nov, tested in the editor): PartForge **ground clearance check** in `--audit` (nothing but the gear may reach below 0.15 m above the gear's lowest point, chassis pieces and every module on every socket); Lifter belly hooks stowed, winch and flak turret lower; kit regenerated, Setup 6-7 run. Hangar wireframes bake again when a model changes (`WireframeLibrary.Entry.SourceStamp`). UI input states: one state table in `ChopperTheme.uss` (rest, hover, focus, active = list open, disabled; list rows incl. checked) — the default theme's grey `:active` box and white hover text no longer show;
   - done (3 Nov, tested in the editor): **fonts → Karnivore** for headings and UI elements (`Assets/Fonts/Karnivore`; Lite = labels, values, buttons, tiles; Bold = titles); **Chakra Petch Regular** (OFL, `Assets/Fonts/ChakraPetch`) for longer text, class `chopper-body`, sentence case (descriptions); Digit (dot) and Seven only as small decoration: `chopper-deco-dot` / `chopper-deco-seven`; used on the hangar tile numbers and a top-bar readout). `TabularText` digit width 0.66 em. Old UGUI prefabs and scenes keep Ethnocentric. **Skins are a tile grid** ("// PAINT": colour swatch, name, EQUIPPED / LEVEL n / pattern), no dropdown; `HangarModel.Skins`, locked skins cannot be picked. Karnivore letter spacing (3 Nov): +1 px all text, +2 px titles, +4 px large titles. Licence checked (3 Nov): Apostrophic Labs License, freeware, use in any context (a sold game too) without permission; do not edit or re-release the font files, do not sell the fonts. Text and sources in `Assets/Fonts/Karnivore/LICENSE.txt`; Karnivore's K looks like H at small sizes;
   - done (3 Nov, compiled, checked in play by Claude, 101/101 tests; the 3 Nov layout compiles too): **hangar polish**: mouse drag on the empty view (not on UI) turns the chopper, it keeps turning a little after release, idle spin waits 3 s; gamepad right stick turns it; with a slot open the drag turns up to 60 degrees away from the socket view and springs back on the next slot. A new chassis builds by a scan line from bottom to top (parts appear, lines glow bright green `HangarScan`, then fade to the overlay); a fitted module scans in and slides onto its socket. Rotors stop while their slot is open and while a new one builds, then spin up again (`HangarSettings.rotorSpinUpSeconds`, 1.2 s). Sounds (placeholders, `SoundLibrary` → Hangar): module fit, remove, slot focus, chassis scan. Tune in `HangarSettings` → Turning / Animation. Owner: listen to the sounds and swap clips on `Data/Audio/Cue_ModuleFit` etc.;
   - done (3 Nov, compiled, checked in play by Claude with screenshots of every slot on all 4 chassis, 101/101 tests): **automatic slot focus views**. The camera frames the module (its mesh bounds), not the socket: distance from the module size (`HangarSettings.focusFill`, at least `focusMinRadius` or `focusMinRadiusFraction` of the chopper length); the module sits in the middle of the gap between the panels (`viewCentre`), not the screen centre. Views: side modules from their side; nose and tail at 3/4; rotor (owner, 3 Nov): no turn and no new angle, the overview camera only moves back until the disc and body fit (`rotorFill`); armour shows the whole chopper; under the rotor the camera stays below the disc (engine). When the chopper is built, each slot tries a few views and takes the first where the module is not hidden by the body or another module (mesh colliders on readable copies baked by Setup step 7 into the `WireframeLibrary`, `Entry.Body`; kit meshes are not readable in a build), then the same from steeply below. Validate warns when the copies are missing. **Lifter engine (owner, 3 Nov)**: the engine module replaces the Lifter's side boosters: the pods keep their shells, lose their built-in exhaust, and get an engine socket on a flange at each pod's back (`SOCKET_Engine_0` right, `SOCKET_Engine_1` left; the second repeats the fitted engine, like the tandem rotor). PartForge `parts.py` (`b.engine_points`, `_sockets(engine_points=...)`), Kit02 exported, Setup 5-7 run, audit 0 problems;
   - next for the hangar: make it the first scene once the old menus are ported; module icons on tiles and cards; lighter bloom if the owner wants thinner lines; show it while matchmaking searches (plan section 4).
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
   - done (29 Oct): **first Edgegap deploy, tested**: image `registry.edgegap.com/superhotdancergames-wffvi2kvblrl/airwing-server:0.1.0` (Docker login on the owner's PC), app `airwingnitro` (image `airwing-server`), version `0.1.0` (port `gameport` 7979/UDP, 0.5 vCPU, 1 GB, Never Restart). The editor joined as a client (Play Mode Tools → Client, deploy IP + external port): spawn, combat, RTT ~40 ms, 0 packet loss, no snapshot gaps, input reaches the server ~2 ticks early. Over 30 s of scripted flight: no hitch except after a 1.2 s editor stall (then 11–15 m catch-up snaps for 0.3 s); corrections p99 1.4 m/frame (local server: 0.85). Fixed on the way: client receive queue 64 → 256 (overflowed while the client loaded), and a dark square on the HUD at low hull (hull arc took the readout's "low" class with its background);
   - secrets (30 Oct): the Edgegap **API token** is in the Unity Dashboard Secret Manager as `edgegap1` (the Cloud Code allocator reads it by that name; never in the repo). The registry token is separate and lives in Docker Desktop (`docker login`); on the owner's PC a second API token is in the PowerShell SecretStore vault "Local" as `EdgegapToken` (read with `Get-Secret EdgegapToken -AsPlainText`; header `Authorization: token <value>`; tested 30 Oct: lists app `airwingnitro`, version `0.1.0`);
   - done (30 Oct): **`0.1.1` pushed and tested** (image `airwing-server:0.1.1`, app version `0.1.1` of `airwingnitro`, same settings as `0.1.0`). A test deployment (Melbourne) loaded the match and the **idle stop ended it by itself** after 5 min with no player. A new app version needs the registry login (Edgegap shows the old one only as "encrypted"); on the owner's PC the vault "Local" holds `EdgegapRegistryUser`, `EdgegapRegistryToken` and `EdgegapToken`. API calls used: `POST /v1/app/airwingnitro/version`, `POST /v1/deploy` (`ip_list` from `GET /v1/ip`), `GET /v1/status/<id>`, `GET /v1/deployment/<id>/container-logs`, `DELETE /v1/stop/<id>`. Seen in the server log at start: "Expected server to always update once per frame when in sleep mode. Ran 2-4 steps" (Netcode warning during the load; likely the 4-ticks-per-frame `NetcodeConfig`); check it does not repeat in a match;
   - done (30 Oct, **tested end to end**): **Unity Matchmaker → Edgegap**. Hangar → [ FIND MATCH ] → ticket on queue `Default` (environment `development` from the editor, `ServiceEnvironment`) with the player's public IP → pool `ffa` (one team, 1-8 players, Cloud Code hosting) → our module `CloudCode/ChopperAllocator` starts `airwingnitro` `0.1.1` near the players (secret `edgegap1`) → IP:port → `LocalSession.Join` (client-only world) → TrainingRange. Tested from the editor: match found in ~10 s, server in Melbourne, RTT 38 ms, in game with a chopper; the server stopped itself 2 min after the player left. Queue and matchmaker settings are files (`Assets/Chopper/Matchmaker/Default.mmq`, `Matchmaker.mme`), deployed with the module from Services → Deployment. Gotchas: a disabled Edgegap app leaves tickets waiting with no match ("NoneAssignment") and no error in the game; the `.mmq` reader needs camelCase keys. See `CloudCode/README.md`;
   - done (31 Oct, tested in the editor, no errors): **search keeps the hangar usable**: while searching, FIND MATCH becomes [ CANCEL SEARCH ] (yellow), the status line shows the step and time (`SEARCHING 0:12`), the loadout can still be changed; Escape backs out of a slot first, then cancels. When a match is found the loadout is saved again (`HangarModel.SaveChoice`, only if valid), so changes made while searching are used at spawn. Test: `Searching_LoadoutStaysEditableAndSaveChoiceFollowsIt`;
   - done (31 Oct, compiled, not play-tested): music volume default 50% (`AudioService.DefaultVolume`; a saved slider value wins). Cockpit view, own gun: no muzzle flash and bullets start 8 m ahead (`GunEffectsSystem`; the world-space flash rose into the canopy when going down). Into / out of a cockpit view the own hull hides at 90% / shows at 10% of the camera blend, not at the key press (`ChopperCameraRig.HidesOwnChopper`);
   - 31 Oct: **server build gotcha (Unity 6.6)**: with Build Profiles → Linux Server → "Build and Run on: Local Machine", Unity's Linux build check fails on Windows and the build stops at once ("Failed: 0 error(s), 0 MB, 0 s"; inside: Entities `BuildContentArchives` → "Unable to build with the current configuration"). Set "Build and Run on" to **RemoteSSH** (no device needed; we only build). Then Setup → Build Linux server works (167 MB, 289 s). Image `airwing-server:0.1.2` pushed 31 Oct;
   - 31 Oct: Edgegap version **`0.1.2`** (owner made it; the account's version limit was reached, so `-InPlace` exists); the allocator starts `0.1.2` (deploy the module to development and production). Release script `Tools/Edgegap/release.ps1` (push, Edgegap version, allocator) written, **not yet run**: the stored `EdgegapToken` got `Unauthorized` on 31 Oct (new token or new account?);
   - **found (31 Oct): double rockets online** (every own rocket drawn twice, online only). Logged from the editor in a development match: the client predicted the shot on tick T, then a snapshot rollback replayed the ticks from the server's cooldown, which was **quantized** (0.001): 0.00033 instead of 0 on tick T, so the replay fired on T+1, a new first-time tick, and drew a second rocket. Fix (**tested online 31 Oct on Edgegap `0.1.2`: 20 shots, 20 rockets, no doubles**): weapon and boost timers that gate firing are unquantized ghost fields (`PylonWeaponState`, `HitscanWeaponState`, `Countermeasures`, `ChopperFlightState.BoostRechargeWait`). **This changes the netcode protocol: the Edgegap server must be rebuilt and pushed (0.1.2) and the allocator's `Version` changed, or clients cannot connect.** Lesson: never quantize a predicted timer that a threshold test reads;
   - done (31 Oct, compiled, did not fix it): **double rocket guard**: online, a shot sometimes drew two rockets side by side. Netcode resets its "last fully predicted tick" record in some frames, and then marks an already predicted tick as `IsFirstTimeFullyPredictingTick` again (package `NetcodeClientPredictionRateManager`). `PylonFireSystem` now also keeps the newest launch tick per pylon (`_launchTicks`, a system field, not rolled back) and draws a predicted rocket / missile only once per pylon and tick. The server was never affected (damage was right);
   - done (31 Oct): **client build scenes**: Setup step 7 puts `Hangar` first (the game starts there) and `TrainingRange`; the old 2023 scenes stay in the list but off (missing scripts; the first client build started in the old `Init`). Validate warns if an old scene is on again;
   - done (31 Oct, deployed to development and production): **queue rule**: pool `ffa` waits for 2 players, then starts with 1 after 20 s (`RangeControl.ReplaceMin`, age Oldest, in `Default.mmq`). Deploy from Services → Deployment. Solo tests now take about 20 s longer;
   - next: production environment (same queue, module and secret), backfill once the server supports it, the server checks the match id and loadouts from the matchmaker, Relay friend matches;
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
   - city building generator (plan section 7a2): **first version built 3 Nov** in `Tools/CityForge` (Blender side, checked with `bpy`; 20 buildings incl. 300–550 m megatowers, twin towers with sky bridges, 3 high-tech landmarks, terrain (desert basin, mesas, canyon, storm-drain channel, tall rim, far backdrop and skyline), a road-first city layout of about 170 buildings (41 building FBX incl. variants), streets, freeway, tunnel portals, sky road loop and spiral, biome terrain shading (6 biomes; `TerrainBiome` shader graph not yet compiled), LODs, colliders, markers, audit, previews with Kit02 choppers for scale). Second map **City04** (style `crater`, 4 Nov): volcanic crater floor, cinder cones, breach, lava moat and streams, refinery buildings (`Ind_Refinery`, `Ind_Cooling`, `Ind_Spheres`); output `Assets/Chopper/Art/City/City04`, step 8 imports it to `Assets/Scenes/City04.unity`. Terrain look (4 Nov): masks and a gully normal map are map-wide textures (`<kit>_TerrainMasks.png`, `<kit>_TerrainNormal.png`, 1.5 m per texel) that the TerrainBiome shader reads by world position (no vertex colours); mesh erosion and boulders were tried and removed. Light and air (4 Nov; first run in the editor 5 Nov: graphs fixed, lights and plumes tuned at night): animated lights in BuildingWear (blink, neon, flame), real point lights, steam/smoke plumes, reflection probes and the map's height fog placed by step 8 from `<kit>.terrain.json`; new step 9 adds the full-screen height fog pass (`CityHeightFog.shader`), SSAO and a post Volume (`Data/Rendering/CityPost.asset`). Step 9 adds the post Volume only to an open match scene that holds a city SubScene. Tuning: `LightLooks` (budget per kind, sum at most 256 for Forward+), `LightIntensityScale`, `PlumeLooks` (incl. `Glow`) in `CityAssets.cs`, and `HEIGHT_FOG` in CityForge. City match scenes (5 Nov, played in the editor: spawn, flight, HUD, night look): Setup step 10 (`Editor/Setup/MapSceneSetup.cs`) builds `Assets/Scenes/Map_<kit>.unity` (bridge, HUD, camera rig with 20 km far clip, gameplay SubScene `Map_<kit>/Map_<kit>_Gameplay.unity` with the spawner 30 m over the mesa nearest the centre and arena bounds from `flightBounds`, the city SubScene, sun and ambient, post volume) and bakes the city probes to `Art/City/<kit>/Probes/<time>/` (step 8 reuses them). `ChopperBootstrap` treats `Map_*` scenes as gameplay scenes. Time of day (Morning, Day, Afternoon, Night) and weather (Clear, Rain, Sandstorm, Fog, Snow, Ash) are enums in `Simulation/Visuals/MapConditions.cs`; their looks are `EnvironmentSettings` on the GameConfig (`Data/Rendering/EnvironmentSettings.asset`). Only Night + Clear exist and are baked into the scenes. Planned, not built: the server picks the conditions per match and replicates them; a client system applies the look at load (sun, ambient, fog tint and density, city lights, wetness, weather particles) and swaps the probe set. Maps (5 Nov): `MapCatalog` (`Data/Maps/MapCatalog.asset`, on the GameConfig; step 10 fills it: Training Range = training only, each city = training and match pool). The hangar has a TRAINING MAP list (saved); TRAINING loads that map. Online: the server's first scene `ServerBoot.unity` (`ServerMapLoader`, made by "Build Linux server") loads `-map NAME|ID` or a random match-pool map; each gameplay subscene carries `MatchMapAuthoring` (map id); `MatchMapAnnounceSystem` sends `MatchMapAnnouncement` to each connection; the hangar waits for it after joining (20 s, then falls back to TrainingRange) and loads that scene. The server build holds the boot scene and every match-pool map. Planned: the matchmaker picks the map (match property → `-map`). Open: the server fell behind its tick rate in the city in the editor (check in a build, with the profiler); with 256 lights, most City04 street lamps have no light pool. Map styles and ideas for more maps: `Tools/CityForge/README.md` (Maps). Traffic (6 Nov, compiled and played in Map_City04: 640 vehicles, queues, trails, searchlights, cranes): CityForge `traffic.py` writes "traffic" into `<kit>.terrain.json` (ground lanes on streets, roads out, freeway, sky road and spiral; flying-car lanes at 55/95/135 m and drone loops round the tallest towers, both checked for clearance against the placed meshes) and exports 4 vehicle models (`Traffic_Car`, `_Truck`, `_Flyer`, `_Drone`). Step 8 adds a Traffic object (`CityTrafficAuthoring`, baked to `CityTraffic`: path blob, LOD0 meshes, `<kit>_Traffic.mat` with GPU instancing, numbers from `Data/Rendering/TrafficSettings.asset`); the client `CityTrafficSystem` moves the vehicles and draws them with `Graphics.RenderMeshInstanced` (no GameObjects, no colliders, not networked). City04 was exported again here (cloud): its layout differs from the Windows export (probably another shapely/GEOS version: other lot splits), so press step 8 and step 10 again for City04. Editor steps: compile; press 8 (both cities) and 10; play a city map at night: cars with cyan/pink lights on the streets, flyers over the main roads, drones round the towers; tune `TrafficSettings` and re-press 8. Then (6 Nov, not compiled): cars queue and stop at red lights before junctions (CityForge "stops" per lane, two phases, cycle 24 s, green 10 s each; car-following with a 9 m gap), flying cars pull pink tail-light trails, searchlights sweep from the tallest roofs and some industrial plots. Both use the new additive shader `Chopper/LightBeam` (`Art/Shaders/LightBeam.shader`); step 8 makes `Data/Rendering/TrafficTrail.mat` and `SearchlightBeam.mat` once (tune colours there). Tick batching checked 6 Nov: not a server load problem (server sim 1.2 ms, frames about 10 ms, worst views max 18 ms); single editor stalls set off the warning, so its window is now 240 frames and the load grace 15 s. Profile a server build for the real number. Preferences > General > Script Changes While Playing is now Recompile After Finished Playing (set by the owner, 6 Nov): a recompile in play probably gives a black screen. Step 10 now loads the config again after opening a scene (the map scenes had no HUD, options or cockpit screens). Turn weight (6 Nov, owner to judge in play): TurnResponse base 12 to 7 (FlightTuning default): Scout 9.1, Gunship 7, Assault 5.6, Lifter 4.55; the 29 Oct handling ratios stay (1.3, 1, 0.8, 0.65). Check: the shader compiles, beams soft where they meet buildings (needs the depth texture), queues clear on green. Then (6 Nov, not compiled): tower cranes (CityForge `cranes.py`: `City_Cranes` masts with colliders, `Crane_Jib` model; the client swings the jibs and draws warm floodlight cones) and heat shimmer over the lava (`Art/Shaders/HeatHaze.shader` reads the URP opaque texture; step 9 turns the opaque and depth textures on for every URP asset, also the Performant one: check the cost there; material `Data/Rendering/HeatHaze.mat`). Press 9 and 8. Unity side **written 4 Nov, not yet run in the editor**: Setup step '8. Import city (CityForge)' (`Editor/Setup/CityAssets.cs`) sets texture imports, makes `City01_Buildings.mat` (BuildingWear graph, URP Lit stand-in) and `City01_Terrain.mat` (TerrainBiome graph), remaps the FBX files, makes one prefab per building in `Prefabs/City/City01` (box colliders, markers, LOD heights) and builds `Assets/Scenes/City01.unity` (terrain chunks with mesh colliders, buildings at their layout spots, backdrop, markers). Editor steps: open the two shader graphs and fix compile errors, press step 8, add `City01.unity` as a SubScene of a match scene, set fog (exponential, density 0.00033) and camera far clip 20000 m, size the arena bounds to `flightBounds` in `City01.terrain.json`.

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
| Combat | `Simulation/Combat/*` (`HitscanFireSystem`, `PylonFireSystem`, `WeaponLimits`, `RocketSystem`, `ApplyDamageSystem`, `EnvironmentDamageSystem`) |
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
| City buildings | `Tools/CityForge` (Blender: `generate.py`, `cityforge/specs.py`, `kit.py`, `buildings.py`, `audit.py`); output `Assets/Chopper/Art/City/City01`; Unity import `Editor/Setup/CityAssets.cs` (step 8) |
| Tests | `Tests/EditMode/*` (HUD models, HUD look, markers, cluster, health, loadout rules, loadout code, guidance, audio, hangar model, wireframe, saved choice, damage model, weapon limits) |
