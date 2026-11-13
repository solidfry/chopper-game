# Chopper game — handoff (updated 9 Nov 2026; old logs in `Docs/HandoffHistory.md`)

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
  - Weapon limits (6 Nov): see history 4a.
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

## 4a. Recent work (details: `Docs/HandoffHistory.md`)

| When | What | State |
|---|---|---|
| 27 Oct | Base build: flight, guns, pylons, HUD, options | compiled |
| 6 Nov | Weapon limits (magazines, heat) | compiled |
| 7 Nov | Repair, pilots 1–5, input actions, scoreboard, impact sounds | written without the editor (see history 4c–4e) |
| 7 Nov | Audio balance, tuning buttons, cockpit boot-up | compiled, run |
| 8 Nov | Solo network test, match log | compiled, run in builds |
| 8 Nov | Bots: pilot, navigator, brain | compiled, tested |
| 9 Nov | Rocket lead always on (dim free aim at the first hit; hidden at open sky), seeker lock grace (`lockGraceSeconds`, 1.5 s), socket scale baked (`ChassisSocket.Scale`), explosion layer under crash sounds, `ChassisClass` | written without the editor |
| 10 Nov | PartForge Kit02 art pass: machined modules (`modules.py`), panel lines, rivets and markings (`surface.py`), a baked texture set per cockpit, cockpit chamfers sized per edge (up to 40 mm; before, the bevel's overlap clamp made them all 4 mm), `cockpit_preview.py --baked 1` | exported, imported in Unity (Setup step 5, no kit problems); not checked in play; details in `Tools/PartForge/ART_DIRECTION.md` |
| 11 Nov | Scene Jumper editor window (`Editor/Tools/SceneJumper.cs`, Chopper > Scene Jumper, Ctrl+Shift+J): open, add, play-from, favourites, recent, play-mode start scene | written without the editor |
| 11 Nov | UI Toolkit port, step 1: home screen (`Home.unity`, Setup step 11): animated city-grid backdrop (`HomeBackdrop`), glitch title, typed boot log, nav (PLAY, TRAINING, HANGAR, PILOTS, OPTIONS, QUIT), profile card with codec scan (`CodecScan`), pilot codex page; options get MAIN MENU, hangar gets < HOME (Escape at overview) | written without the editor |

| 11 Nov (later) | Home screen run in the editor: fixed the blank screen (an XML comment with `--` broke `Home.uxml`); the old logo (four layers in `UI/Home/Logo`, wings slide in, NITRO glitches); a live city behind the menu: step 11 builds `Assets/Scenes/HomeCity.unity` from City04 (buildings and near ground only, no colliders or scripts) with the `Chopper/TerminalWire` shader (world grid lines, rim glow, lit windows, climbing scan band; materials `Data/Rendering/HomeCityWire.mat`, `HomeGroundWire.mat`), `HomeFlyover` orbits it, `HomeTerminal` post (green channel mix, bloom, grain), CRT layer (`CodecScan`, now plain quads: the Painter2D fill drew half a rectangle) | run in the editor (screenshots), not in a build |

| 11 Nov (evening) | Mode select: PLAY opens SELECT MODE (`UI/Home/ModesPage.cs`, `Modes.uss`): one card per mode in `Presentation/Menu/GameModeCatalog.cs` (`Data/Modes/GameModeCatalog.asset`, made by Setup step 11): Quick Play and Free For All live, Team Deathmatch and Capture the Nuke OFFLINE. DEPLOY saves the mode (`LoadoutChoiceStore.MatchMode`) and opens the hangar; FIND MATCH joins the mode's `Queue` (empty = `HangarSettings.MatchQueue`) and shows `SEARCHING // <MODE>`. Q / E step modes; new prompt tokens `{Prev}` / `{Next}`. UniStorm imported earlier the same day, not wired in | compiled, HomeModelTests pass, run in the editor (page and DEPLOY to hangar) |

| 11 Nov (night) | Toast notices: `Presentation/Flow/NotificationFeed.cs` (root scope singleton, lives on `SceneFlow`, so notices survive scene loads; max 3 shown, queue, repeats count up), `UI/Notifications/*` (own panel next to the transition overlay, top right, slide in, timer bar, click to dismiss), sounds `Cue_Notice` / `Cue_NoticeError` (Setup step 1). Post from scene code with `PresentationContext.Notify(title, text, NoticeKind)`. Posted now: sign-in failed (offline mode), matchmaking failed, pilot set. Disconnect handling: see the next row | compiled, NotificationFeedTests pass, run in the editor |

| 11 Nov (late) | Disconnect handling: `Simulation/Session/ConnectionWatch.cs` (client system reads `NetworkStreamDriver.ConnectionEventsForTick`, makes a `ClientDisconnect` event entity); `PresentationEventSystem` publishes `ConnectionLost` and keeps the entity until a scene's presentation hears it (a loss during a scene load reaches the next scene). `PresentationContext` shows a toast (`Presentation/Flow/ConnectionNotice.cs`: text per reason, e.g. old server version, server closed the match, timeout) and, in a match, goes to Home (`LINK LOST`, worlds stopped once covered); in the hangar while joining it stops the dead worlds and FIND MATCH shows `CONNECTION FAILED`. Known gap: a loss while the match scene waits for your chopper waits for the reveal (max hold) before it leaves | compiled, ConnectionNoticeTests pass; not run in play (owed: join a dead address, stop a local server, kill an Edgegap deployment mid-match) |
| 12 Nov | Build size 1382 → 1075 MB and warnings: city meshes Low compression (step 8); City04 wall town as 15 instanced shack models through the layout (`shanties.shack_models`, `State.part_instances`; thinner decks, stairs, cables, step 1.6°); Kit02 detail maps 1024, occlusion BC4 (`KitAssets.Map`); `TextureBudget` (Setup button, Do all, Validate) caps third-party textures at 2048 / AO 1024 (.meta only); HomeCity copies unpacked, colliders and markers dropped (was 2489 missing scripts); UniStorm, TMP, PostProcessing v1 and our shader warnings fixed. `Docs/PlaytestChecklist.md` | client built (0 errors); not checked in play (wall town look, menu backdrop, frame rate with 2122 shacks); Linux server build started but stopped: PC low on memory, editor left on Linux Server target |
| 13 Nov | Sky, weather, time of day, step 1: PostProcessing v1 removed; UniStorm got assembly files (`UniStorm`, `UniStorm.Editor`); server picks time and weather per match from the map entry (`MapCatalog` Times/Weathers → `MatchMap` masks → `MatchConditionsPickSystem`, `-time`/`-weather` overrides) and sends them with the map announcement; client `EnvironmentSystem` applies them through the scene's `EnvironmentDriver` (UniStorm driver, own assembly `Chopper.Presentation.UniStorm`, UNISTORM_PRESENT) or the plain look; editor preview override on EnvironmentSettings; globals `_ChopperCityLights`, `_ChopperWetness` (not read by shaders yet) | written without the editor |
<<<<<<< HEAD
| 13 Nov (later) | Spawn system: `SpawnPointAuthoring` points per map (step 4 adds 12 round the spawner on a map with none; step 10 makes 12 on city maps: mesa tops, then the widest gaps of the circle), `SpawnSettings` (GameConfig → spawner → `SpawnRules`), `SpawnScoring` (pure: blocked / occupied / score / ties), `SpawnProbe` (physics: wall distance, ground height, open sky, enemy line of sight). `ChopperRespawnSystem` is now the only spawn path (a new player gets a `RespawnTimer` of 0 s) and picks the point when the timer ends. Fallbacks: drop the enemy distance rule, then the spawner's own point (with a warning). Validate: maps with fewer than 12 points, points outside the bounds or near a wall (loaded colliders only). Not included: spawn protection, team spawns (hook: `SpawnThreat.Enemy`) | written without the editor |
**Editor steps owed (13 Nov, spawn):** compile; Setup 1 (makes `Data/Spawn/SpawnSettings.asset`, links it to the GameConfig); open TrainingRange, Setup 4 (spawner gets the settings; 12 points in a 150 m ring round the spawner), then move the points by hand to good places (open the gameplay subscene; each point shows a green sphere and facing line); Setup 10 (city maps get 12 points: look at the ones that are not over a mesa, they are 120 m over the highest mesa); Validate; run SpawnScoringTests; play with 2+ thin clients or bots: check the spread at the start, respawns away from enemies, and the server log for `[Spawn]` lines (a warning = no point could be used).

=======
| 13 Nov | Terrain: alpine and ravine styles, erosion, budget mesh detail, detail masks (CityForge, plan 6c) | Python run and checked; not in Unity |
>>>>>>> origin/v2
**Editor steps owed (13 Nov, sky):** compile (UniStorm now has its own assemblies: watch for missing references); Window > UniStorm > Add URP Renderer Features (on every URP renderer used); in MapCatalog give each map its Times and Weathers; press Setup 10 (adds the Environment driver and the allowed sets to each map); play a map with EnvironmentSettings preview override on (try Day + Rain), then off; check fog (UniStorm fog plus our height fog may be too thick), city lights at day, frame time; run MatchConditionsTests. Server: `-time Day -weather Rain` forces the pick.

**Editor steps owed (11 Nov, home screen):** Setup 1, 4 and 11 and Validate done by Claude through MCP (11 Nov, 0 problems; step 4 added no training targets, so they were there). Still owed: play Home: boot log, nav, codex, options, training, hangar and back; run HomeModelTests. The old `MainMenu` scene is untouched until the clean-up step.

**Owed after 10 Nov:** look at the cockpits in play (bevels, edge wear: interior wear is half of the hull's, `texture.py` factor 0.5); Setup step 1 (chassis class is empty on the 4 chassis) and step 4 (no training targets in the gameplay subscene). The 9 Nov compile error (`ChassisClass` enum under `CreateAssetMenu`) is fixed.

**Pilot portraits (10 Nov):** the art in `Assets/Textures/Pilots` (image + `_n` normal map) is a live portrait: `PilotPortraitRig` (Presentation/Pilots) draws it into a 256x384 render texture with `Chopper/PilotPortrait`. The "rig" is a shader warp round face points (`PilotFace` on each `PilotDefinition`, measured by hand, set by `PilotAssets.LinkArt` in Setup step 1): breath, blinks, idle head motion, lean with roll, look into turns, jaw from the voice RMS of the pilot's own radio line (`IAudioService.VoiceLevel`; Wraith's mask glows instead), hit shake/flash, jam glitch. Light: the scene sun and ambient probe seen from the pilot's facing (chopper in a match, hangar camera on the select screen), level scaled to about 1; accent screen glow from below; part accent duotone, scanlines, rolling band. Shown in the HUD bottom left (`LocalPilotPortraitSystem`, opacity group PILOT PORTRAIT) in chase and cockpit views, and on the pilot select big card (roster tiles show the still art). Look and motion numbers: `HudLookSettings` → Pilot Portrait. Checked: editor test renders of all pilots. Later on 10 Nov: in the HUD it is in the UI green (`HudColour`, full duotone; the select screen keeps the pilot accent), smaller (104x156) at the bottom centre under the flight ring, with stronger world light (`SunBoost` 4, `LightContrast` 1.8), much less head motion, and clear scanlines (52 lines, depth 0.55). The hangar has no directional light, so on the select screen the portrait gets only the ambient light. Roster tiles: the face framed from the face points (`PilotCard.FaceFrame`), tinted UI green. **Window transition (shared):** `UI/Theme/WindowTransition.cs` + `chopper-window*` classes in ChopperTheme.uss: a CRT power on (a bright line grows from the middle, then opens) and power off (falls in to a line, the line shrinks out). Used by the pilot select screen; use `WindowTransition.Open/Close(element, hiddenClass)` for other windows (options, scoreboard). **Owed:** check it in play (HUD corner with the sphere look, select screen, talking on an ability).

**City reflection probes (10 Nov):** they now live in each match scene (`Map_<kit>`), not in the city SubScene. In the SubScene they were companion objects, made again on each domain reload, and URP culled a destroyed probe (NullReference in `ReflectionProbeManager.UpdateGpuData`, then ZBinningJob errors) after each recompile. `CityAssets.PlaceProbes` is called by step 10, and step 10 removes the old probes from the city scenes. Step 10 was run for City01 and City04 and the probes were baked again. After a recompile the console is clean. Owed: check in play that the reflections still look right.

**Editor steps still owed (9 Nov work):** compile (done 10 Nov); reimport the chopper prefabs (socket scale); let Unity reimport `Assets/Sound/Impacts`; run GuidanceTests; in play check the dim free-aim reticle, a lock that survives a short jink, and crash sounds. Older owed steps are in the history file (4c–4e).

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

**Owner TODOs (8 Nov):**
- ~~**A good spawn system**~~: built 13 Nov (see 4a), written without the editor. Next: check it in play, tune `SpawnSettings`, hand-place the TrainingRange points; later spawn protection and team spawns.
- **Bot pathfinding**: bots that avoid buildings and the ground, find and chase targets, so solo tests have real combat (see history 4g: the scripted pilot crashes every 7 s). This also gives Claude a way to fly the game well in tests. Plan section 7a has the bot design (utility decisions, air and land). **Steps 1 (pilot), 2 (navigator) and 3 (brain, first version) done, see history 4h**; next tuning, missiles, server bots.
- **Healing look (12 Nov)**: while a chopper heals, a green sheen pulses over the hull (material) and green particles rise; maybe a small repair drone that flies to the chopper and heals it. Hooks: `ChopperRepair.State == Repairing` (replicated, `Simulation/Combat/Repair.cs`) already drives the repair sparks in `DamageEffectsSystem.EmitRepair`; the sheen fits the hull shader (`ChopperSkin.hlsl`) as a per-renderer value; settings on `CombatEffectsSettings` / `DamageLook`.

**Next task: first real playtest** (recommended 6 Nov, owner agreed): 2–4 people on client builds, on a city map, through Edgegap.
- **Why first:** network tests so far used thin clients and one editor. It answers weapon balance and magazine sizes, turn weight per chassis, hits on other players, and missiles, flares and locks against people. It also checks server load: one local reading was 53 % of a CPU core with 1 player, and the Edgegap version has 0.5 vCPU.
- **Needs:**
  1. A Windows client build. It is the first with the current maps, so it may show new problems.
  2. The allocator deployed to production, or test on development. Clients must be built from current code.
  3. ~~A server log of tick time and CPU.~~ Done: `LoadLogSystem` (`[Load]` line every 30 s, always on in a dedicated server, `-loadLog` elsewhere; also the `load` event in the match log) gives players, ticks/s, frames/s, worst frame, hitches, CPU % of a core, memory, and since 11 Nov the sim work per tick (mean, worst) and its % of the tick budget (`LoadLogTickStartSystem` stamps the tick start). Read it with `Tools/Edgegap/fetch-logs.ps1`.
  4. A short list to judge: balance, feel, hitches, map flow.
- **After it:** bots if the playtest was fun (they also make solo testing easier), or Capture the Nuke for the signature mode sooner: the owner decides. Visual polish and the hangar callouts wait.

Then continue with this order (step 1 onward). Older note (29 Oct evening): first Edgegap deploy works (step 4, Phase 3); owner TODOs for the damage model and death break-up are in plan section 7d. The feel pass and style guide work of 28 Oct is done; its follow-ups (feel tuning, match-end, lobby and server list screens from Figma) are the backlog in plan section 7d.


**Open lines of work** (full notes per item: history file, old section 6):
1. **Modular build (Phase 4):** server checks skin unlocks; part upgrade chains; chassis variants (quad Lifter: socket scale and `ChassisClass` done; next a second Lifter spec in PartForge `kits.py`, its handling row, hangar cards grouped by class).
2. **Damage model** (plan 7d): compiled and played 6 Nov; owner tuning.
3. **Weapons:** rocket systems (slow heavy, fast light, aim-guided, smart, salvo) and a rich roster of guns and abilities (plan section 5).
4. **Hangar:** first scene once old menus are ported; module icons; show it while matchmaking searches.
5. **HUD:** tune the look in play; team colours and objective markers once TDM exists; more weapon markers.
6. **Services (Phase 3):** production environment; backfill; server checks match id and loadouts; Relay friend matches.
7. **Modes (Phase 6):** TDM, then Capture the Nuke; bots on the server.
8. **Progression (Phase 5):** Cloud Code grants, Cloud Save, unlocks, leaderboards, Steam achievements and Steam names in feed and scoreboard (`MatchHudModel.PlayerName` is the hook).
9. **Any time:** port old menus to UI Toolkit and delete `Assets/Scripts`; export `.blend` models to `.fbx`; delete or rebuild old scenes; city maps (CityForge, plan 7a2, see history); map conditions (time of day, weather) picked by the server.


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
| Bootstrap / spawn | `Simulation/ChopperBootstrap.cs`, `Simulation/Session/*` (incl. dev `SessionDiagnosticsSystem`); spawn points: `SpawnPointAuthoring`, `SpawnSettings`, `SpawnScoring`, `SpawnProbe`, `ChopperRespawnSystem`, setup `Editor/Setup/SpawnAssets.cs` |
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
| Tests | `Tests/EditMode/*` (HUD models, HUD look, markers, cluster, health, loadout rules, loadout code, guidance, audio, hangar model, wireframe, saved choice, damage model, weapon limits, spawn scoring) |
