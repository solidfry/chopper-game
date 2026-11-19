# Chopper game — handoff (updated 19 Nov 2026; old logs in `Docs/HandoffHistory.md`)

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
- **Loadout data:** 4 chassis with flight tuning, module definitions and 21 weapon definitions (count 19 Nov). `LoadoutRules` validate and compute stats. The prefab bakes a default Gunship loadout.
- **Services:** sign-in (Steam ticket or anonymous), Unity Matchmaker queues (`Default`, `tdm`), a Cloud Code allocator and Edgegap hosting (played online 18-19 Nov, development environment), a VContainer root scope. Steam code compiles only when Steamworks.NET is installed.
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
- **Stale Burst code (seen 28 Oct, twice):** the compiled Burst job can keep running with an old layout. This happens after `ChopperInput` changes (input buffer garbled: bools lost, chopper ignores input). It also happens after a field is added to a struct that a Burst job reads (e.g. `ChopperFlightConfig`): forces are silently wrong (chopper "has no force"). Fix: stop play, delete `Library/BurstCache` (or close Unity first), recompile. Quick check: turn off Jobs → Burst → Enable Compilation; if the bug goes away, it is the cache.
- A `ComponentLookup` field set in `OnCreate` can run stale in Burst (NRE). Use `SystemAPI.GetComponentLookup` in `OnUpdate`.
- Never start a long build from an MCP `execute_code` call: the editor deadlocks. Queue it with `EditorApplication.delayCall`.
- Test windows need `-profile pN`, else they share one account (matchmaker 429). A killed searching window leaves its ticket in the queue.

## 4a. Recent work (details and full text: `Docs/HandoffHistory.md`, section "4a table")

Short list. One line per item. Read the history file for the full row.

| When | What | Commits |
|---|---|---|
| 27 Oct - 16 Nov | Base build to UI port: flight, weapons, limits, pilots, bots, progression 1-4, spawn, sky, home screen, mode select, toasts, audio tool, briefing. | see history |
| 17 Nov | Server bot combat test: bots fight, 0 crash deaths. Burn kills now show the weapon name. | `1c88bcc5` |
| 17 Nov | Healing look (green hull sheen, rising motes). | `90316028` |
| 17 Nov | Gun bullet travel, gun lead ring, bots lead rockets, kill credit after the shooter dies (two reviews). Human play test still owed. | `c04f0a44`, `c8ff753f`, `624fe75b`, `bef31a58` |
| 17 Nov | Flares as real decoys, XP popups, 2070 audio, music in Git LFS, Cloud Code deployed, full screen escape menu, cockpit screen fix. | `97925d45`, `010238bd`, `e9f2579a`, `f694ebcb`, `e4ab6194`, `5da9b4a0` |
| 18 Nov | Repair drone, Edgegap playtest builds (image `0.1.5`), online smoke test with 2 bot windows. | `e9d00108`, `95b15eed`, `40f2d9d2` |
| 18 Nov | TDM steps 1-4 (teams, HUD, matchmaker queue `tdm`), release 0.1.6, prediction tick batching. | `9e82cdb7`, `ab3c36ef`, `bd8bb437`, `11af25d0`, `cfe7b85d`, `cca870c3` |
| 18 Nov | City look, Buto volumetric fog, perf (city traffic and downwash Burst jobs), effects pass. | `b7f2f143`..`a60a8c9e`, `d752226d` |
| 19 Nov | Owner requests: scoreboard per team, topple damage, centred UI text, terminal pills, cursor, Buto shadow-map fix, client zip `Builds/AirwingNitro_Windows_7c9ce447.zip`. | `7a876b50`, `9b89bc57`, `9d47240b`, `c02e5665`, `2ed35945`, `b14b6940`, `0263f358` |
| 19 Nov | Chopper death steps 1-3 (falling wreck, break-up, blast push, wreck parts), project tidy to `Assets/ThirdParty`. | `0057e351`, `2491eea4`, `43eb280c`, `c9290eff`, `df625df3` |
| 19 Nov night | Respawn timer per mode, harder crash rules, lights key L, stylised moon, Buto fog masks (lava, vents), bot re-test (Rocket Pod 14 %, missiles 85 %). | `14bf509d`, `0c2baa44`, `50ffa87d`, `1d929db7`, `2a9b6cdc`, `e7fe1589`, `dcab2c39` |
| 19 Nov night | Rocket Pod 3 m proximity fuse (owner todo 1). Committed; EditMode 389/389; not flown. | `1ff0f4ca` |

**Rebuild server and client before any online test.** Ghost fields, RPCs and input changed on 18 and 19 Nov.

## 5. Open problems

| Problem | Likely cause | Next action |
|---|---|---|
| Burst "not a known entry point" messages | Stale Burst cache after big code changes | Clear `Library/BurstCache` |
| Only tested with thin clients, bot windows and one editor on Edgegap (29 Oct: 4 players, 170 ms, 2% loss, smooth; 18 Nov: 2 bot windows, RTT 36-53 ms) | - | A test with people, in client builds (editor stalls cause catch-up snaps). Not high priority now (owner, 19 Nov) |
| MessagePipe without a DI container (`PresentationEvents`) | API not checked in an editor before | Watch for errors on first message |
| URP render errors in the console, seen once on 6 Nov after a recompile: NullReferenceException in `ReflectionProbeManager.UpdateGpuData`, InvalidOperationException for `ZBinningJob` | Unknown. Stack is editor view rendering, not game code | Did not return in the next play run. If they return, note what was open and running |
| Cloud Save write after a match (progression step 4) never confirmed | The 18 Nov Edgegap run logged no `[Progress]` line: no `UGS_*` variables on the deployment | Owner: do `CloudCode/README.md` dashboard steps 1-2 (service account, secrets `ugs_key_id` and `ugs_secret_key`, development environment). No redeploy needed for the secrets |
| Allocator is on the development environment only | Production not set up | Phase 3 work (see open line 6) |

Solved and removed from this table: Server Tick Batching warnings (29 Oct), Edgegap idle stop and old protocol (18 Nov, image `0.1.5`). Details in the history file.

## 6. Next steps (recommended order)

**Owner decision, 19 Nov night.** The first real playtest is not high priority now. Do these four:

1. **Rocket Pod 3 m proximity fuse.** Another session did this on 19 Nov night (`1ff0f4ca`: Rocket Pod and Mk2 burst within 3 m of an air target; not flown). Keep it listed until it is checked in play or in a bot test.
2. **Missile test with a bot flying the human chopper.** Missiles are the top killer (85 % of kills in the 19 Nov bot test). Check lock, flares, decoys and evade from the bot side. **Done 19 Nov 22:25** (TrainingRange, 4.5 min, bot flies the human chopper via `ScriptedPilotOverride` added by `execute_code`; match log turned on by reflection on `MatchLogSystem`): 94 missiles, 86 decoyed by flares, 76 expired, only **2 missile kills** (was 11 of 19 when the human sat idle). Flares used 42 times. Kills were mostly minigun (8 of 15). Rocket Pod with the 3 m fuse: 37 shots, 14 % (same as before; small sample). 2 landing deaths. No exceptions. **Owner (19 Nov):** flare decoy is OK; have fewer flares. Done: `Ability_Flares` 2 per load (was 3), reload 25 s (was 18 s), cooldown 3 s and decoy 3.5 s same. Measured 19 Nov 23:00: missile kills 2 of 15 -> 18 of 46. Rebuild server before online tests.
3. **Measure each bot profile.** Three profiles in `Assets/Chopper/Data/Bots` (Rookie, Default, Ace). Use the match log. Compare hit rate, kills and deaths per profile. **Done 19 Nov 22:35** (TrainingRange, 14.3 min, 3 runs of ~4.7 min; each run rotates the 3 profiles over the 3 bot slots by setting `BotSkill` in `execute_code`, so each profile flies each roster loadout once; a Default bot flies the human chopper):
   - Ace: 23 kills, 11 deaths, 0 crashes, 21 % accuracy, 29 flares.
   - Default: 9 kills, 10 deaths, 1 crash, 15 % accuracy, 27 flares.
   - Rookie: 9 kills, 15 deaths, 5 crashes, 16 % accuracy, 38 flares.
   - (Hits count burn ticks, so accuracy is approximate.) **Finding:** Ace is clearly stronger. Rookie and Default aim the same and kill the same; Rookie only dies more (mostly crashes). **Done 19 Nov 23:00:** Rookie now reacts at 0.7 s, aim error 10 degrees, settle 3.5 s, sees 650 m, gun heat release 0.95 (`BotAssets` and asset). Re-measured (runs 1-2 of the rotation complete, run 3 cut at 50 s; fewer flares too): Ace 22/7, Default 11/11, Rookie 4/15. Missiles: 18 of 46 kills (was 2 of 15), 181 of 232 decoyed (78 %, was 91 %).
4. **Second-client leave test.** A second client joins and a server bot must leave. **Passed 19 Nov 22:45** (TrainingRange, editor): a thin client made by `ClientServerBootstrap.CreateThinClientWorld()`, connected to port 7979, with the `Gameplay` subscene loaded into its world (it needs `ChopperSpawner`), went in game as player 2; `[Bots] Bot 1002 leaves (2 player(s), 2 bot(s) wanted)`, bots 3 to 2. On its disconnect, `Bot 1002 (GLITCH) joins` again, bots back to 3. One exception came from the test setup only ("Connection to server already initiated": the thin world connected once by itself too). Not tested: match end with bots; a real second window. **Fixed after:** training started from a menu (`LocalSession.Start`) now makes the thin clients set in Multiplayer → Play Mode Tools, each with one connect request, before the map loads (tested: thin client in game, bots 2, 0 console errors). The earlier exception was from adding a second connect by hand: NetCode's `ConfigureThinClientWorldSystem` already connects a thin world to the client's last address. Burst `Debug.Log` lines show a native stack, but they are logs, not errors.

**19 Nov late (owner reports, fixed):**
- Explosion smoke and fire stretched and bent: the pack's smoke used stretched billboards (each puff turned to face its own motion) and every layer got a full random 3D rotation. Now `CombatEffects.SpawnLayer` keeps layers upright (random heading only) and draws stretched billboards with no speed or camera stretch as billboards. The pack's smoke shader already has a depth fade (soft particles).
- Fire stayed in the air after an air blast: new `BlastLayer.Ground` (flames and smoke column). `WreckEffectsSystem` traces the wreck's fall (debris drag, static world only); in the air these layers start where it lands, after the fall time; no ground under it = not shown. Checked in play (TrainingRange): air blast clear in the air, flames and column upright on the ground, 0 errors.
- **Effects bench (owner request):** Setup window "Open effects bench scene" (`Assets/Chopper/Scenes/EffectsBench.unity`, built by `EffectsBenchScene`). Plain scene, no netcode: ground, blocks, sun, city post look, orbit camera (`BenchOrbitCamera`: right drag, wheel, F1-F4 views). `EffectsBench` inspector: "Play the bench", ground and air blast, missile, rocket and gun impact, each explosion layer alone, clear; keys 1-6, R, C, [ ]. It runs the game's own code: the blast timing moved from `WreckEffectsSystem` to `BlastSequence` (shared; the game casts against ECS static bodies, the bench against scene colliders).
- Cards that faced other choppers (owner): the big flames were flat quads drawn as mesh particles with "Facing" alignment; Unity turns mesh particles once per frame for one camera. `CombatEffects.SpawnLayer` now draws quad mesh particles and vertical billboards as camera billboards. Checked in the bench: flames face the camera, the full blast reads as orange fire, no flat red block.
- **Explosion look, found with the bench (19 Nov, owner checked):** the pack's flame card is a flat 8x8 plane (not a 4-vertex quad), so it stayed a mesh particle; now any flat card mesh is a camera billboard. Billboards keep only screen-plane spin (the packs' 3D start rotation turned cards edge-on: the "long slim" smoke streaks; flames lay flat: the red patch from above). Wreck flame emission a third (90, 130, 240). New `BlastLayer.Tint` (start colour, keeps fades): smoke column dark (0.3), scale 3.2 (start 1.2). Commits `29a1d5b1`, `f5b62c7b`, `a41e3ca3`, `a7cd2e49`.
- Editor errors seen when play stopped: "The entity does not exist" comes from NetCode's editor analytics (`PlayStateNotifier.CollectGhostTypes`) reading a ghost prefab that no longer exists at exit (Unity package, editor only). "Mismatched state in DefaultScenario" is a Multiplayer Play Mode assert after a recompile that ran right after play stopped. Neither is game code; both go away on the next play.
- Edgegap server log: 306 NetCode warnings "Expected server to always update once per frame when in sleep mode" in 27 min, with sim spikes of 43-57 ms. In the editor (2 min, 3 bots, every server system timed) the worst server frame was 6 ms. Most likely the Edgegap shared CPU, not our code; check `[Load]` lines on a bigger Edgegap machine before changing code.

**Owner checks owed (play or listen):**
- Moon, fog masks, crash feel, L key (lights), new cursor: not judged yet.
- Lock warning, missile warning, flares and new SFX: listen in play.
- Repair drone: try it in the hangar (fit it on a pylon). Tune `Weapon_RepairDrone` and the orbit in `CombatEffectsSettings`. Allies as targets: done (`RepairDrone.PickTarget`: a hurt team mate under the reticle in team modes, else itself); not checked in play. TODO (owner): consider moving countermeasures to their own slot.
- Gun bullet travel: human play test (lead ring shows, hits land, pulse laser still instant). With 2 clients, check that the ring leads a crossing player.
- Online TDM: a full match in a build is not checked. Production allocator is still to do.

**Open lines of work** (full notes per item: history file, old section 6):
1. **Modular build (Phase 4):** server checks skin unlocks; part upgrade chains; chassis variants (quad Lifter: socket scale and `ChassisClass` done; next a second Lifter spec in PartForge `kits.py`, its handling row, hangar cards grouped by class).
2. **Damage model** (plan 7d): compiled and played 6 Nov; owner tuning. Crash and topple rules made harder 19 Nov (`0c2baa44`, `9b89bc57`).
3. **Weapons:** roster step 1 compiled and played 17 Nov (4 guns, 4 rocket types, bombs, air mines, 2 Mk II chain tiers; plan section 5, "Step 1 as built"). Ground Seeker and Cluster wait for ground units. Still open: aim-guided and smart rockets (need a ghost), per-weapon effects and sounds, more abilities, weapon stats in the hangar, tuning. Rocket Pod and Mk II are 210 m/s, 2.7 s (14 Nov).
4. **Hangar:** first scene once old menus are ported; module icons; show it while matchmaking searches.
5. **HUD:** tune the look in play; objective markers; more weapon markers. (TDM team scores, ally markers and team scoreboard done 18-19 Nov.)
6. **Services (Phase 3):** production environment; backfill; server checks match id and loadouts; Relay friend matches.
7. **Modes (Phase 6):** TDM done and online (release 0.1.6, queue `tdm`). Next: Capture the Nuke. Server bots done. Bots still open: ground units.
8. **Progression (Phase 5):** steps 1-4 built and compiled; steps 1-3 played 17 Nov (gate, results bars, XP saved once). Step 4 (Cloud Save write) needs the Edgegap server account (see section 5). Still open: Cloud Code grants, Cloud Save, unlocks, leaderboards, Steam achievements and Steam names in feed and scoreboard (`MatchHudModel.PlayerName` is the hook).
9. **Any time:** port old menus to UI Toolkit and delete `Assets/Scripts`; export `.blend` models to `.fbx`; delete or rebuild old scenes; city maps (CityForge, plan 7a2, see history).

**Later (not high priority): first real playtest.** 2-4 people on client builds, on a city map, through Edgegap. Client zip `Builds/AirwingNitro_Windows_7c9ce447.zip` (19 Nov 12:51, Buto fix and cursor) is **out of date**: it lacks the respawn timer and lights (new RPC and ghost field), crash rules and fuse. Build a new client and server first. Server: image `airwing-server:0.1.6` on Edgegap version `0.1.3` (`release.ps1 -Version 0.1.6 -InPlace 0.1.3`; rebuild and repoint after each protocol change). Server log: `LoadLogSystem` `[Load]` lines; read with `Tools/Edgegap/fetch-logs.ps1 -Watch 60`. Checklist: `Docs/PlaytestChecklist.md`. After it: Capture the Nuke or more bot work (owner decides). Old full block: history file.

Older note (29 Oct): owner TODOs for the damage model and death break-up are in plan section 7d. Feel tuning, match-end, lobby and server list screens from Figma are the backlog there.

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
| Special views | `Simulation/Combat/Optics.cs` (`VisionMode`, `VisionMath`), `Presentation/Camera/VisionLook.cs`, `VisionEffects.cs`, `VisionSystem.cs`, `VisionGlow.shader`; state and key: `ZoomState.ToggleVision`; new mode = enum value + `VisionMath.Signal` case + `VisionLook.Default` case + a module row in `WeaponRosterAssets` |
| Zoom | `Simulation/Combat/Optics.cs` (`OpticsConfig`, `ZoomMath`), `Simulation/Definitions/OpticsDefinition.cs`, `Presentation/Camera/ZoomState.cs`, key in `PresentationContext.Tick`, FOV in `ChopperCameraRig.Follow`, turn scale in `ChopperInputSystem`; tests: `ZoomTests` |
| Weapon roster | Behaviours: `Simulation/Combat/PylonFireSystem.cs`, `RocketSystem.cs`, `Rockets.cs` (`RocketFlight`), `HitscanWeapon.cs` (`GunDamage`); data: `Simulation/Definitions/RocketWeaponDefinition.cs`, `HitscanWeaponDefinition.cs`; content: `Editor/Setup/WeaponRosterAssets.cs` (new weapon = a row there, or an asset by hand); tests: `WeaponRosterTests` |
| Progression | `Simulation/Progression/Progression.cs` (`LevelCurve`, `XpRates`, `MasterySplit`), `ProgressionSettings.cs`, `LoadoutGate.cs` (`PlayerProgress`, `LoadoutGateSystem`); server record: `Presentation/Profile/ServerProgress.cs`, `Services/Auth/ProfileCloud.cs`, `CloudCode/ChopperAllocator/Project/ProgressStore.cs`; `Presentation/Profile/*` (`PlayerProfile`, `ProfileRules`, `ProfileService`, `ProfileUnlocks`); `Presentation/Results/MatchXp.cs`; locks: `Simulation/Loadout/LoadoutRules.cs`, `LoadoutTypes.cs` (`IPlayerUnlocks`); setup: `Editor/Setup/ProgressionAssets.cs` |
| Flight | `Simulation/Flight/ChopperFlightSystem.cs`, `FlightTuning.cs`, `ChopperAuthoring.cs`, `ChopperPredictionSmoothing.cs` |
| Combat | `Simulation/Combat/*` (`HitscanFireSystem`, `PylonFireSystem`, `WeaponLimits`, `RocketSystem`, `ApplyDamageSystem`, `EnvironmentDamageSystem`) |
| Match | `Simulation/Match/MatchScoreSystem.cs` (players by `PlayerId`, `Simulation/Session/ConnectionOwner.cs`) |
| Bots | `Simulation/Bots/*`: shared step `BotFlight`; client `BotPilotSystem`; server `ServerBots.cs` (`ServerBot`, `BotIds`, `BotFill`, `BotNames`), `ServerBotFillSystem`, `ServerBotSystem`, `BotSettings`; setup `Editor/Setup/BotAssets.cs`; data `Data/Bots` |
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
| Tests | `Tests/EditMode/*` (HUD models, HUD look, markers, cluster, health, loadout rules, loadout code, guidance, audio, hangar model, wireframe, saved choice, damage model, weapon limits, spawn scoring, bot fill) |
| Audio tool | `Editor/AudioEffects/*` (`CyberpunkAudioProcessorWindow`, `AudioEffectProcessor`, `AudioEffectChain`, `EditorAudioPreview`, `EditorTransportIcons`); window **Tools > Audio > Cyberpunk Audio Processor**; docs `Docs/AudioProcessorTool.md` |
