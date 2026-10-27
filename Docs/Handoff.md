# Chopper game — handoff (27 Oct 2026)

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
- **Options overlay** (Escape / gamepad Start): HUD look preset, reduce flicker / effects, 5 volume sliders. While open: cursor free, flight input neutral (`PlayerInputBlock` singleton, written by `InputBlockSystem` from `PresentationContext.MenuOpen`). First piece of the UI Toolkit menu port.
- **Prediction smoothing** (own chopper): server corrections fade out over a few ticks instead of a snap (`ChopperSmoothingAction`, Netcode `GhostPredictionSmoothing` on `LocalTransform`). Each correction removes 25% of the position error and 35% of the rotation error; errors over 10 m snap (respawn). Velocity is not smoothed. Tune on `ChopperAuthoring` → "Network smoothing". Missiles are not smoothed. The Simulation assembly now allows unsafe code (needed for the smoothing callback).
- Decisions taken (owner): Battlefield-like missile ghosts; auto lock, some missiles cannot dumb-fire; cycle the selected pylon; one ghost per chassis; pooled GameObject module visuals; sockets before the CRT HUD.

### Editor steps after pulling
1. Chopper → Setup → **Do all**, then **Validate**. Fix what it lists.
2. Fix compile errors (nothing from 27 Oct is compiled yet). Send the console list to Claude if needed.
3. Open `TrainingRange.unity`, play. Check: chopper spawns, minigun / rocket pod / missile / flare art sits on the chopper, locks and missiles work, flares break locks, wheel switches pylon. Markers sit on the training targets; the lead circle shows with the rocket pod selected. Fit "Cluster Missiles" (change the default loadout or the chassis defaults) and fire it at a target: 8 bomblets.
4. HUD look: the HUD should glow, with scanlines and a slight curve. The phosphor trail is off on a `HudLookSettings` asset made before this change: set Persistence (0.5) and Persistence Time (0.12) on the CRT 84 profile, or delete the asset and run Do all. Take a hit: the HUD glitches. Die: it drops out. If the HUD is gone or black: set the preset to Clean on `Data/Hud/HudLookSettings` (default preset) or clear the PlayerPrefs key `Chopper.HudLook.Preset`, and send the console to Claude. Check the HUD edges for dark fringes (alpha mode).
5. Press Escape: the options overlay opens, the cursor shows, the chopper stops taking input. Change HUD look and volumes; press Escape again. Fit "Swarm Pod", lock a target, fire: 10 missiles fan out.
6. Move sockets in `ChopperGhost` → Sockets if the art sits in the wrong place, then run *Do all* again (it does not move sockets).
7. Network feel: Multiplayer Play Mode or a build, Network Simulator 150 ms / 2% loss. Fly hard turns and dashes. Small corrections should be soft, not jumps. Too floaty: raise the smoothing values; still jumpy: lower them, or look for a client/server mismatch in the flight code.
8. Run edit-mode tests (Window → General → Test Runner).
9. If tick-batching or Burst messages come back: clear `Library/BurstCache`.

## 5. Open problems

| Problem | Likely cause | Next action |
|---|---|---|
| "Server Tick Batching" warnings (1.25–1.75 ticks/frame) | Editor below 60 FPS; maybe the Burst cache is stale | Clear `Library/BurstCache`, check Game view Stats FPS, test in a build, profile if it stays |
| Burst "not a known entry point" messages | Stale Burst cache after big code changes | Same: clear `Library/BurstCache` |
| Not tested with more than one player | — | Multiplayer Play Mode, 4 players, Network Simulator 150 ms / 2% loss |
| MessagePipe without a DI container (`PresentationEvents`) | API not checked in an editor before | Watch for errors on first message |

## 6. Next steps (recommended order)

1. **Finish Phase 2 (needs Unity):** the 4-player network test, the tick-batching fix, and the edit-mode tests.
2. **Phase 4: modular build.** Plan section 5, "Modular build: lego sockets":
   - done (not yet compiled): sockets, module visuals, the loadout on the ghost, each player's loadout at spawn;
   - next: tune socket positions in Unity; own model and sockets per chassis; rotor spin on rotor modules;
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
- HUD: retro 80s look — CRT / vector-display shader or post-processing over the HUD (plan section 3b).
- Weapons: heat-seeking missiles (air and ground), cluster missiles and other cool cyberpunk weapons; a dynamic HUD overlay with targeting (plan section 3b).
- Choppers will become fully modular ("lego" modules on chassis sockets). The current chopper is a placeholder.
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
| Options | `UI/Options/*` (overlay), `Presentation/Bridge/InputBlockSystem.cs`, `Simulation/Input/PlayerInputBlock.cs` |
| Tests | `Tests/EditMode/*` (HUD models, HUD look, markers, cluster, health, loadout rules, loadout code, guidance, audio) |
