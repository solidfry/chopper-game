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

**Editor steps still owed (9 Nov work):** compile; reimport the chopper prefabs (socket scale); let Unity reimport `Assets/Sound/Impacts`; run GuidanceTests; in play check the dim free-aim reticle, a lock that survives a short jink, and crash sounds. Older owed steps are in the history file (4c–4e).

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
- **A good spawn system**: spawn points spread over the map, away from enemies and walls, safe height; today all players spawn near one point.
- **Bot pathfinding**: bots that avoid buildings and the ground, find and chase targets, so solo tests have real combat (see history 4g: the scripted pilot crashes every 7 s). This also gives Claude a way to fly the game well in tests. Plan section 7a has the bot design (utility decisions, air and land). **Steps 1 (pilot), 2 (navigator) and 3 (brain, first version) done, see history 4h**; next tuning, missiles, server bots.

**Next task: first real playtest** (recommended 6 Nov, owner agreed): 2–4 people on client builds, on a city map, through Edgegap.
- **Why first:** network tests so far used thin clients and one editor. It answers weapon balance and magazine sizes, turn weight per chassis, hits on other players, and missiles, flares and locks against people. It also checks server load: one local reading was 53 % of a CPU core with 1 player, and the Edgegap version has 0.5 vCPU.
- **Needs:**
  1. A Windows client build. It is the first with the current maps, so it may show new problems.
  2. The allocator deployed to production, or test on development. Clients must be built from current code.
  3. A server log of tick time and CPU. First check whether `SessionDiagnosticsSystem` already gives this.
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
