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

## 5. Open problems

| Problem | Likely cause | Next action |
|---|---|---|
| "Server Tick Batching" warnings (1.25–1.75 ticks/frame) | Editor below 60 FPS; maybe the Burst cache is stale | Clear `Library/BurstCache`, check Game view Stats FPS, test in a build, profile if it stays |
| Burst "not a known entry point" messages | Stale Burst cache after big code changes | Same: clear `Library/BurstCache` |
| Not tested with more than one player | — | Multiplayer Play Mode, 4 players, Network Simulator 150 ms / 2% loss |
| MessagePipe without a DI container (`PresentationEvents`) | API not checked in an editor before | Watch for errors on first message |

## 6. Next steps (recommended order)

1. **Finish Phase 2 (needs Unity):** the 4-player network test, the tick-batching fix, and the edit-mode tests.
2. **Phase 4: modular build.** Design in plan section 5, "Modular build: lego sockets":
   - sockets on the chassis;
   - module prefabs;
   - the loadout sent on the ghost;
   - each player's own loadout applied at spawn;
   - then the green wireframe hangar.
3. **Targeting, guided weapons and the dynamic HUD** (plan section 3b):
   - server lock system with heat seekers and HUD lock brackets;
   - guided missile ghosts, flares;
   - air-to-ground missiles, cluster and swarm missiles;
   - world markers and a lead indicator;
   - retro 80s HUD look (CRT shader pass over the HUD, glitches driven by gameplay).
   It can run in parallel with step 2: the weapons are modules.
4. **Phase 3: services:**
   - Steamworks.NET and the Dashboard Steam provider;
   - Multiplayer Services sessions with a Cloud Code allocator for Edgegap;
   - a dedicated server build;
   - party and rejoin.
5. **Phase 6: modes:** TDM, then Capture the Nuke, then bots.
6. **Phase 5: progression:** Cloud Code grants, Cloud Save, unlocks, leaderboards and Steam achievements.
7. **Any time:**
   - port the old menus to UI Toolkit and delete `Assets/Scripts`;
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
| Flight | `Simulation/Flight/ChopperFlightSystem.cs`, `FlightTuning.cs`, `ChopperAuthoring.cs` |
| Combat | `Simulation/Combat/*` (`HitscanFireSystem`, `RocketSystem`, `ApplyDamageSystem`, `EnvironmentDamageSystem`) |
| Match | `Simulation/Match/MatchScoreSystem.cs` |
| Loadout | `Simulation/Loadout/*`, `Simulation/Definitions/*`, data in `Assets/Chopper/Data/Loadout` |
| Presentation | `Presentation/Bridge/*`, `Events/*`, `Feedback/*`, `Audio/*`, `Effects/*`, `Hud/*`, `Visuals/*` |
| UI | `UI/Hud/ChopperHud.uxml/.uss`, `ChopperHudView.cs`, theme in `UI/Theme` |
| Editor | `Editor/Setup/*` (window, asset factories, validator) |
| Tests | `Tests/EditMode/*` (HUD models, health, loadout rules, audio) |
