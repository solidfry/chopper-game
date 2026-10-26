# Chopper Game — Multiplayer Overhaul, Customisation & Progression Plan

## Context

**Last worked on: 10 Dec 2023** (commit `a91a30fb`). Unity 2022.3.14f1, Netcode for GameObjects (NGO) 1.7, UGS Matchmaker + Multiplay. Last work: "build 21", music/SFX, main-menu items, score stop at match end. Only Free For All exists. Team/lobby code is commented out (`Networking/LobbyManager.cs`). `Enums/Chassis.cs` (Light/Medium/Heavy) and `PlayerData` SO (level, XP, K/D) are placeholders with no logic.

**Why change:** the current netcode is slow, easy to cheat, and its server host is gone.

Problems found:
1. **Every bullet is a `NetworkObject`** — spawn/despawn + server Rigidbody + a networked impact particle per hit (`PlayerAttackManager.FireServer`, `AmmoEffectServer.DoDestroy`, `ServerParticleHandler`). Largest bandwidth/CPU cost.
2. **Client-authoritative movement** (`Networking/ClientNetworkTransform.cs`). No prediction or reconciliation (see TODO in `PlayerManager.HandleMovement`).
3. **Client controls its own health**: `NetworkHealth.SetPlayerHealthServerRpc`, client-reported crash and out-of-bounds damage.
4. **Hit detection = server physics collisions** on Rigidbody bullets. No lag compensation, so shots that hit on screen miss on server.
5. **Firing bugs** (`WeaponSlot.Firing`): loop does not yield while cooldown > 0 (can freeze the main thread); the same `IEnumerator` is reused, so firing can fail after the first stop.
6. **Jobs misuse**: `TempJob` alloc + `Schedule().Complete()` each frame for 3-line maths (`MovementController`, `AmmoEffect*`, `HandleProjectileRb`). Slower than plain code.
7. `DoShakeCameraClientRpc` goes to all clients; only the owner needs it.
8. **Multiplay hosting was shut down on 1 Apr 2026** — `Server/ServerStartup.cs` cannot get allocations. `AwaitAllocationId` also has an inverted check.
9. Duplicated `MAXPLAYERS` (`ConnectionApprovalHandler`, `ServerStartup`); global static `GameEvents` hide data flow.

**Decisions (from user):** Unity 6.6, Netcode for Entities (built in) + Edgegap hosting behind Unity Matchmaker. Steam for identity and friends.

### Locked decision: move to Netcode for Entities
- **Status:** final. No prototype or go/no-go gate. No fallback to Netcode for GameObjects.
- **Why:** built-in prediction, rollback and lag compensation (the hardest parts for a fast physics shooter); much lower bandwidth and server CPU; built into Unity 6.6; Unity's direction (NGO 3.0 runs on Netcode for Entities from 6.7 LTS); small codebase, and the netcode needs a rewrite anyway.
- **Accepted costs:** ECS learning curve; flight feel must be re-tuned for Unity Physics; hybrid ECS simulation + GameObject presentation bridge; fewer tutorials and assets.
- **Rule:** no new gameplay or netcode work in NGO. NGO code stays only until its ECS replacement exists, then it is deleted.

---

## 1. Target tech stack

| Area | Choice |
|---|---|
| Engine | Unity 6.6 (6000.6, supported release, out 1 Sep 2026), URP. Move to 6.7 LTS when it ships |
| Simulation | Entities + Netcode for Entities (built-in core package since 6.5, version fixed to editor) + Unity Physics |
| Presentation | GameObjects for UI, HUD, audio, VFX, Cinemachine 3 (via ghost "presentation" GameObjects) |
| Transport | Unity Transport (UTP), DTLS on |
| Services | `com.unity.services.multiplayer` (Sessions + Matchmaker), Authentication, Cloud Save, Cloud Code, Leaderboards, Remote Config |
| Hosting | Edgegap, started by a Cloud Code allocator from Matchmaker |
| Steam | Steamworks.NET → session ticket → Unity Authentication Steam provider |
| Tools | Netcode Profiler (official since 6.5), Multiplayer Play Mode (replaces ParrelSync), Network Simulator |

Unity 6.6 features to use:
- Built-in Netcode for Entities (editor requires the built-in version).
- Native `Dictionary` serialisation — use for module/stat tables in SOs.
- **Watch out:** new projects default to no domain reload on Enter Play Mode. Static state (`Events/GameEvents.cs`, `Utilities/Singleton.cs`) will not reset. ECS removes most of this; reset any remaining statics with `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`.
- Future: NGO 3.0 runs on Netcode for Entities 7.0 from 6.7 LTS. Our ECS choice follows Unity's direction.
- Netcode Profiler modules: prediction/interpolation ticks per snapshot.
- `GhostVariantBakedOverride` to override ghost component sync in bakers.
- Thin client worlds — run 20–30 bot clients in one editor for load tests.
- Prefab registration before world creation (simpler loadout prefab setup).

Remove: `com.unity.services.multiplay`, `com.unity.services.matchmaker` (now in multiplayer package), `com.unity.multiplayer.samples.coop`, ParrelSync, `com.unity.ide.vscode`.

## 1b. Code modernisation (all code, not only netcode)

**Goal:** modern, testable code. No global singletons where possible.

Current state: 25 scripts use `Singleton<T>` / `SingletonPersistent<T>` / `SingletonNetwork<T>` (`Utilities/Singleton.cs`), `.Instance` or `NetworkManager.Singleton`. `Events/GameEvents.cs` has 41 static events used as a global bus.

### Rules
- **No `Singleton<T>` base classes.** Delete `Utilities/Singleton.cs` at the end of the port.
- **ECS singletons are allowed.** `SystemAPI.GetSingleton<T>()` (match state, game mode config, nuke state) is per-world data, not a global object. It works with server/client worlds and thin clients.
- **GameObject side uses dependency injection** with **VContainer**: one root `LifetimeScope` (services: auth, matchmaking, Steam, save, audio, settings) and child scopes per scene (menu, hangar, match). Classes get dependencies by constructor or `[Inject]`, behind interfaces (`IMatchmakingService`, `IPlayerProfileService`, `IAudioService`, `INotificationService`).
- **Plain C# services, not MonoBehaviours**, when no Unity lifecycle is needed (e.g. `MatchmakerClient`, `ProfileLoader` logic, `ColourManager`).
- **Replace static `GameEvents`:**
  - Simulation events (kill, death, hit, nuke state) → ECS components/buffers, read by presentation systems.
  - UI/presentation events → typed message bus scoped by VContainer (VContainer + MessagePipe), or ScriptableObject event channels for designer-wired events.
  - No `static event` / `static Action` fields.
- **Async:** use `Awaitable` (Unity 6) or UniTask with `CancellationToken` (cancel on scene unload / destroy). No `async void` except UI event handlers. No polling loops with `Task.Delay` (current `MatchmakerClient.PollTicketStatus`).
- **No domain reload safe:** no static mutable state, so Unity 6.6 fast Enter Play Mode works without bugs.

### Other modern practice
- **Assembly definitions** per feature (`Chopper.Simulation`, `Chopper.Presentation`, `Chopper.Services`, `Chopper.UI`, `Chopper.Tests`). Faster compile, clear dependencies (presentation can read simulation, never the reverse).
- **C# style:** `readonly` structs for data, `sealed` classes by default, file-scoped namespaces, nullable annotations on new service code, `[SerializeField] private` fields (no public fields like `Weapon.firingCooldownTimer`).
- **Data:** ScriptableObjects for design data (chassis, modules, weapons, modes) → baked to ECS blob assets. Native `Dictionary` serialisation in 6.6 for lookup tables.
- **UI:** move **all** UI to UI Toolkit (see below). No new UGUI.

### UI Toolkit migration (all UI)
- **Scope:** every screen in `Assets/Scripts/UI/**` and `UI/HUD/**` — main menu, navigation, game mode select, notifications, profile/avatar, scoreboard, match menu, end-match screen, timers, transitions, and the in-flight HUD. Plus the new hangar and progression screens.
- **Structure:** UXML for layout, one shared USS theme (green cyberpunk: colour tokens, fonts, glow borders, scanline overlay) + per-screen USS. Reusable custom controls with `[UxmlElement]` (Unity 6): `StatBar`, `StatTile`, `CyberButton`, `NotificationToast`, `PlayerCard`, `CountdownTimer`.
- **Data binding:** use Unity 6 runtime data binding (`dataSource` + `[CreateProperty]`) to view-model classes. ECS presentation systems write values (health, speed, altitude, score, timer) into the view models; UI never reads ECS or netcode directly.
- **HUD:**
  - Speed, altitude, health, ammo, timer, kill feed → screen-space UI Toolkit panel.
  - Compass and reticle (now `CompassShapes.cs`, `Reticle.cs` with Shapes) → custom `VisualElement`s drawn with `Painter2D` (vector API).
  - World markers (enemies, nuke, bases) → screen-space elements positioned from world-to-screen each frame (pooled), or world-space UI Toolkit panels where 3D placement is needed (`CanvasMovement.cs`, `WorldPlane.cs`).
- **Navigation/input:** UI Toolkit focus navigation with Input System UI module; gamepad + keyboard + mouse. Replaces `NavigationManager`, `NavButtonHandler`, `ButtonManager`, `UIKeyPressEvent`, `UIButtonPressEvent`.
- **Transitions:** USS transitions + `experimental.animation` for screen fades and the glitch effect (replaces `TransitionController` / `TransitionData`).
- **Audio:** UI sounds from a small `UISoundBinder` that hooks control callbacks to `IAudioService` (replaces `UIAudioClipRequester`).
- **Glow:** MK Glow post-process does not affect overlay UI Toolkit panels. Get neon glow from USS (layered borders, `text-shadow`, soft gradient images) or render the panel to a RenderTexture inside the camera stack where real bloom is needed (hangar).
- **Shapes asset:** keep only if the hangar 3D scene still needs it; remove from UI.
- **Order:** new screens (hangar, progression, matchmaking/party) first in UI Toolkit; then menus; HUD last, during Phase 2 when health/ammo come from ECS. Delete each UGUI prefab/script when its UI Toolkit version works.
- **Input:** keep the generated `PlayerControls` class; read input in one ECS input system that writes the `ICommandData`.
- **Quality:** `.editorconfig` + Roslyn analysers; Unity Test Framework edit-mode tests for services and loadout validation, play-mode tests for netcode (prediction, lag-comp tick alignment).
- **Remove dead code:** commented-out `ServerStartup.cs`, `TempNetworkGameManager.cs`, commented blocks in `LobbyManager.cs`, unused jobs (`Weapons/Jobs`, `PlayerInteraction/Jobs`).

### Order
Do this together with the ECS port, feature by feature — not as a separate pass. Each feature that moves to ECS also moves its GameObject side to DI and drops its singleton/static events. New code (services, hangar, progression) follows these rules from day one.

## 2. Netcode architecture

- **Dedicated server, server-authoritative.** Server tick 60 Hz simulation, snapshot send 30 Hz (`ClientServerTickRate`).
- **Own chopper: client-predicted.** Input is an `ICommandData` (thrust, yaw, pitch, roll, dash, fire bits, aim). Movement code runs in `PredictedSimulationSystemGroup` on client and server. Netcode for Entities rolls back and replays on mismatch.
- **Other choppers: interpolated** (~100 ms buffer, auto-tuned by netcode).
- **Physics:** Unity Physics predicted. Port `MovementController`, `VehicleStabiliser`, `Dash` force maths into a Burst `IJobEntity` (this is where jobs give a real gain — all choppers in one pass).
- **Bandwidth:** ghost importance by distance + relevancy cut-off; quantise rotation/position; static ghosts for pickups/bases.
- **Health, death, respawn, score: server only** ghost fields. Client never writes health.
- **Crash and bounds damage computed on server** from server physics contacts and positions (replaces `NetworkHealth.CalculateEnvironmentalDamage` and `PlayerBoundsHandler` RPCs).

## 3. Hit detection (recommended model)

"Favour the shooter, within limits" — the standard used by modern shooters.

**A. Fast weapons (machine gun, autocannon) — hitscan with lag compensation**
1. Client fires in a predicted input tick. It shows muzzle flash, tracer, sound at once (cosmetic only).
2. Server reads the shot with the client's `InterpolationDelay` (the tick the player saw).
3. Server ray-casts against `PhysicsWorldHistorySingleton` at that tick (enable `EnableLagCompensation` in `NetCodePhysicsConfig`).
4. Cap rewind at **200 ms**. Players with higher ping must lead targets. This stops "shot behind cover" for the victim.
5. Use short ray **segments per tick** with travel time and drop if the gun needs a ballistic feel (still lag-compensated, still no ghost per bullet).

**B. Slow weapons (rockets, missiles, bombs) — predicted projectile ghosts**
- Predicted spawn on the shooter's client, matched with the server ghost by spawn tick + id.
- Server simulates; no target rewind (others can dodge what they see).
- Area damage from `AmmoStats.ExplosionRadius` with server overlap test.

**C. Feedback**
- Hit marker and damage numbers only from a server `HitConfirm` event (sent to shooter) — no fake hits.
- Victim gets damage direction indicator + camera shake (owner-only event).
- Impact VFX: clients play from replicated hit events. No networked particle objects.

**D. Anti-cheat basics**
- Server checks fire rate, ammo, heat, and that the aim ray starts near the server muzzle position.
- Rate-limit inputs; drop clients that send too many.

## 4. Matchmaking — seamless flow

Flow: **Launch → Steam auto sign-in → Main menu → Play (one button) → Loadout screen while searching → Match → Results → Play again (stays in party).**

- **Steam sign-in:** Steamworks.NET gets a session ticket → `AuthenticationService.SignInWithSteamAsync`. No login screen.
- **Party:** Multiplayer Services Session as party. Invite through Steam friends overlay (`SteamFriends` rich presence + join-game command line). Party joins matchmaking as one ticket.
- **Queues:** `quickplay` (any mode), `ffa`, `tdm`, `ctn`. Rules: team size, skill band widening over time, region via Edgegap/QoS latency.
- **Server start:** Matchmaker → Cloud Code allocator → Edgegap deploy → IP:port back to clients. Keep a small pool of warm servers in peak hours to cut wait time.
- **Backfill:** keep current backfill idea, port to new SDK. Join-in-progress allowed until 50% of match time.
- **Reconnect:** store session id; on crash/disconnect offer "Rejoin" for 2 min; server keeps chopper slot.
- **Bots** fill empty slots in FFA/TDM so matches start fast (bots = thin-client-style AI using same input command).
- Server reads matchmaker payload: mode, team assignment, player loadouts.

## 5. Chopper customisation

### Chassis (4)
| Chassis | Role | HP | Speed | Slots |
|---|---|---|---|---|
| **Scout** (Light) | Flank, scout, fast nuke runs | Low | Very high | Nose, 1 pylon, 1 utility |
| **Gunship** (Medium) | All-round | Mid | Mid | Nose, 2 pylons, 1 utility |
| **Assault** (Heavy) | Tank, base defence | High | Low | Nose, 2 pylons, 1 turret, 1 utility |
| **Lifter** (Support) | Winch, repair, best nuke carrier | Mid | Mid-low | Nose, 1 pylon, 2 utility, winch |

### Modules
- **Rotor** — agility vs top speed.
- **Engine** — thrust, boost/dash charges, heat.
- **Armour** — HP vs weight.
- **Nose weapon** — MG, autocannon, laser (hitscan).
- **Pylons** — rockets, homing missiles, bombs, mines (projectile).
- **Turret** (Assault only) — auto-aim flak.
- **Utility** — flares, smoke, repair drone, EMP, shield.
- **Cosmetic** — paint, decals, rotor trails (no stats).

### Rules
- **Weight budget** per chassis. Weight changes speed/turn rate (keeps choices meaningful).
- Data: `ChassisDefinition`, `ModuleDefinition`, `WeaponDefinition` ScriptableObjects (extend `Weapons/ScriptableObjects/WeaponType.cs`, `AmmoType.cs`, `WeaponStats`). Baked to blob assets for ECS.
- Loadout = list of small ids (`ushort` per slot). Sent in matchmaker ticket; **server validates** ownership + weight before spawn.
- Stats applied as ECS components (`ChopperStats`, `WeaponSlot` buffer). Visuals attach module meshes to chassis sockets.
- Up to 5 saved loadouts per player; swap at respawn.

### Customisation UI (hangar scene)
- **Look:** green cyberpunk HUD (dark background, neon green lines, scanlines, glitch on change). Reuse MK Glow (`Assets/_MK`) and Shapes (`Assets/Shapes`) for neon lines and UI frames.
- **Chopper view:** 3D wireframe render of the chassis. URP full-screen/renderer feature or a wireframe Shader Graph (barycentric edges) in green. Installed modules render solid-glow; empty sockets pulse.
- **Socket data:** each chassis prefab has named socket transforms (`Nose`, `PylonL`, `PylonR`, `Turret`, `Utility`, `Rotor`, `Engine`, `Armour`). Each socket also has a **focus pose** (camera position, FOV, chopper rotation).
- **Focus behaviour:** select a slot → Cinemachine 3 blends camera to that socket's virtual camera (zoom in), chopper turntable rotates to the socket's rotation (eased, ~0.4 s). Back → return to overview orbit. Idle → slow auto-rotate.
- **Slot in/out:** module mesh slides/snaps into socket with a short "lock" animation + SFX; old module slides out. Wireframe "scan" sweep over the changed part.
- **Side panel:** module list (owned/locked), stat bars with before/after delta (speed, agility, HP, weight budget bar). Locked items show unlock level.
- **Input:** mouse, keyboard and gamepad (Input System UI navigation; reuse `UI/NavigationManager.cs`, `NavButtonHandler.cs`).
- **Code:** `HangarController` (state: Overview / SlotFocus / ChassisSelect), `SocketFocusPose` component, `LoadoutPreview` (spawns module meshes on sockets). Data comes from the same SOs as gameplay. Loadout save → Cloud Save.
- Also show this screen while matchmaking searches (Section 4).

## 6. Account progression

- **Player level (XP)** from match play: kill, assist, objective (nuke carry/arm/defuse), win, match complete.
- **Chassis mastery level** per chassis → unlocks that chassis's modules and cosmetics.
- **Currency** (earned only, no real money at first) → buy cosmetics / module variants.
- **Stats:** existing `PlayerData` fields (kills, deaths, assists, wins, losses, matches) move to Cloud Save.
- **Server-authoritative grants:** dedicated server posts match results to Cloud Code (service account); Cloud Code writes XP/currency/stats. Client can only read.
- **Leaderboards:** per season, per mode. Skill rating (Glicko-2 or TrueSkill-style) stored in Cloud Save, used by Matchmaker.
- **Steam:** achievements + stats via Steamworks (mirrors key milestones). Steam avatar/name for profiles (`UI/PlayerAvatarImage.cs`, `ProfileLoader.cs`).
- **Remote Config** for XP rates, unlock costs, weapon tuning without new builds.

## 7. Game modes

Shared: server `MatchStateMachine` (port `GameLogic/NetworkMatchStateMachine.cs` + states `InitialisingGame` → `PreGame` → `StartGame` → `InProgressGame` → `PostGame`). `GameMode` SO extended with `ModeType`, score limit, time limit, respawn rules, team count.

- **Free For All** — port as-is (score from `Networking/ScoreManager.cs`).
- **Team Deathmatch** — 2 teams (`Enums/Team.cs`), team spawns, friendly fire off, first to N team kills or most at time-out. Revive `TeamDataBuilder` and lobby team UI.
- **Capture the Nuke** — 2 teams, each with a base.
  - States: `AtSpawn → Carried(team) → Dropped(30 s, then reset) → Arming(team, progress) → Armed(team) → Carried to enemy base → Detonated (round win)`.
  - **Carry:** chopper hooks nuke by winch (slung-load physics). Carrier is slower and cannot use pylons. Lifter carries faster.
  - **Arm:** hold the nuke in your base arm zone for 20 s. Progress pauses if enemies are in the zone. Progress decays if empty.
  - **Attack:** armed nuke must be flown to the enemy base and dropped in their zone. Enemy can shoot it down (drops it) or defuse it in their zone (hold 10 s).
  - Best of 3 rounds. Score bonus for carry/arm/defuse feeds progression.
  - ECS: `NukeState` ghost component (server-owned), trigger zones via Unity Physics triggers, HUD markers on all clients.

## 7b. Status of `v2` branch (checked 25 Oct 2026)

`origin/v2` = 1 commit on top of `main`: `fa63b932` "v2 fixes" (26 May 2026). 474 files; most are asset-store materials re-saved by the upgrade.

| Plan item | v2 status |
|---|---|
| Upgrade engine | **Done to 6000.3.10f1 (6.3 LTS)**, not 6.6 yet |
| Package updates | **Done**: URP 17.3, Cinemachine 3.1.6, Input System 1.18, NGO 2.11.2, Multiplayer Tools 2.2.8, Test Framework 1.6 |
| API fixes for upgrade | **Done**: `rb.velocity` → `linearVelocity` (`Dash.cs`, `VehicleStabiliser.cs`); `Cinemachine` → `Unity.Cinemachine` + noise API (`CameraShakeManager.cs`, `PlayerCameraManager.cs`, `PlayerManager.cs`, `OutputHudValues.cs`) |
| Remove Multiplay | **Done**: `services.multiplay`, `services.matchmaker`, coop samples, vscode removed. `ServerStartup.cs` fully commented out |
| Multiplayer Services package | **Added** (`com.unity.services.multiplayer` 2.2.2) but no code uses it yet |
| Matchmaking | **Off**: `MatchmakerClient` sign-in hook commented out; no server start path |
| Firing bugs, server authority, hit detection | Not started |
| Netcode for Entities / ECS | Not started (still NGO) |
| Steam, Edgegap, customisation, progression, TDM/CTN | Not started |
| ParrelSync | Still in manifest (remove; use Multiplayer Play Mode) |

**Effect on roadmap:** start all new work from `v2`, not `main`. Phase 0 is ~70% done. Left in Phase 0: 6.3 → 6.6 upgrade, add Entities / Netcode for Entities / Unity Physics, remove NGO + ParrelSync, fix `WeaponSlot` firing bugs (only if NGO build must stay playable during the port), delete the commented-out `ServerStartup.cs`.

## 7c. Progress log (25 Oct 2026)

**Phase 0 — done.**
- Unity 6.6 (6000.6.7f1). MK Glow, Buto, GameAnalytics removed (incompatible). Renderers on Forward+.
- Packages: Entities, Entities Graphics, Netcode for Entities, Unity Physics (6.6.0), VContainer 1.19.0, MessagePipe 1.8.2, UniTask 2.5.11, MCP for Unity.
- NGO and ParrelSync removed. NGO gameplay code kept for reference in `Assets/Scripts/_LegacyNGO~` (not compiled).
- `.gitattributes`, `.editorconfig`, `Chopper.*` assembly definitions (`Assets/Chopper/README.md`).
- `ChopperBootstrap`: menus run with a local world only; `TrainingRange` scene starts a local server + client.

**Phase 1 — in progress.**
- Done: `TrainingRange` scene (ground, pillars, targets), owner-predicted chopper ghost (`Assets/Chopper/Prefabs/ChopperGhost.prefab`), GoInGame spawn, input, chase camera, Rigidbody interpolation (fixes per-tick jitter).
- Flight model (`ChopperFlightSystem`): Battlefield 6 Little Bird target — gravity ×1.6, collective, tilt compensation, rate-based rotation, anisotropic drag, weathervane, dash. No flight assist (auto-level off).
- Controls: W/S collective, Left Ctrl descend, A/D yaw, mouse = pitch/roll (aim-style, 0.12 deg/px), arrows also pitch/roll, Shift dash.
- **Parked: flight feel tuning.** "Feels better" — revisit later. Knobs: `ChopperAuthoring` on the prefab, `MouseDegreesPerPixel` + pitch invert in `ChopperInputSystem`, camera FOV/pull-back on `ChopperCameraRig`.
- HUD (26 Oct, written without the editor — needs a compile + play check): UI Toolkit speed/altitude/dash panel. `ChopperHudModel` (bindable view model) + `LocalChopperHudSystem` in `Chopper.Presentation/Hud`; `ChopperHudView` + UXML/USS in `Chopper.UI/Hud`; shared theme `UI/Theme/ChopperTheme.uss`. Add it with menu **Chopper → UI → Add HUD To Open Scene** (creates `UI/Settings/ChopperPanelSettings.asset`). Edit-mode tests: `ChopperHudModelTests`.
- Left: Network Simulator test at 150 ms, editor "Server Tick Batching" warnings (profile).

**Phase 2 — started (26 Oct, written without the editor — needs a compile + play check).**
- `Simulation/Combat`: `Health` (server-written ghost field), `DamageEvent` buffer (server only), `HitscanWeaponConfig/State`, `HitConfirmRpc`.
- `HitscanFireSystem`: predicted fire rate; server ray-cast against `PhysicsWorldHistorySingleton` at the shooter's interpolation delay, capped at 200 ms. Falls back to no rewind if the history is missing.
- `ApplyDamageSystem` (server): applies damage, sends `HitConfirmRpc` to the shooter (hit / kill).
- `ChopperRespawnSystem` (server): dead chopper despawns, new one after `ChopperSpawner.RespawnDelay` (3 s). Spawn code shared in `ChopperSpawning`.
- Input: `ChopperInput.Fire` from `Flight/Fire1` (LMB, Q, RB). Health and gun baked from `ChopperAuthoring` (100 HP, 750 rpm, 8 dmg, 600 m) until Phase 4 modules.
- HUD: health readout (red when low), gun reticle at the aim point, hit/kill marker from server confirms only.
- `TrainingTargetAuthoring`: server-only damageable target that resets after 3 s, for single-player hit tests.
- Editor steps: add `NetCodePhysicsConfig` (Lag Compensation on) to the TrainingRange subscene; add `TrainingTargetAuthoring` to the targets; add `ArenaBoundsAuthoring` and size it to the map.
- Crash damage (`EnvironmentDamageSystem`, server): from speed loss per tick, so colliders need no collision events. Out-of-bounds damage from `ArenaBoundsAuthoring` (box in the subscene); HUD shows "RETURN TO COMBAT AREA".
- Kill credit: `LastAttacker` — a crash/bounds death within 5 s of a hit counts as the attacker's kill.
- FFA match (`Simulation/Match`, server): `MatchScoreSystem` keeps kills/deaths per connection, timer (600 s), score limit (20), post-game (10 s), then next match. Clients get `ScoreRpc`, `KillFeedRpc`, `PlayerLeftRpc`, `MatchStateRpc`. Optional `MatchRulesAuthoring` in the subscene. No damage after the match ends.
- HUD match layer (stays visible while dead): timer, your K/D, leader, kill feed, VICTORY/DRAW banner. `MatchHudModel` + `MatchHudSystem`.
- Rockets (RMB, E, LB): `RocketFireSystem` (predicted fire rate), `RocketSystem` (server: one ray segment per tick, direct + splash damage with falloff, no self damage). Rockets are not ghosts: straight flight, so clients draw them from `RocketLaunchRpc` (others) or `LocalRocketLaunch` (own, at once). `Presentation/Effects/RocketVisualSystem` uses placeholder shapes; swap for prefabs/VFX Graph.
  - Deviation from section 3B: no predicted projectile ghosts. Simpler and cheaper; revisit for guided missiles (they need ghosts).
- Gun tracers: `HitscanWeaponState.ShotCount` ghost field; `Presentation/Effects/GunTracerSystem` draws a pooled line per new shot for every chopper. Placeholder; swap for VFX Graph (muzzle flash, impact).
- Hits taken: server sends `DamageTakenRpc` to the victim only → camera shake (`ChopperCameraRig.AddShake`) + red damage direction ring on the HUD (`DamageFeedbackSystem`).
- Left: real VFX/audio, player names (Steam, Phase 3), 4-player Network Simulator test of all of Phase 2.

**Phase 4 — started (26 Oct, data and rules only; written without the editor).**
- `Simulation/Loadout`: `ChassisDefinition`, `ModuleDefinition`, `LoadoutCatalog` (ScriptableObjects) → plain `ChassisSpec`/`ModuleSpec`.
- `LoadoutRules.Validate` (server check: chassis/module ids, slot type, chassis-only modules, level, ownership, weight budget) and `LoadoutRules.Compute` (health, weight, lift/turn/dash multipliers with weight penalty). Edit-mode tests: `LoadoutRulesTests`.
- Menu **Chopper → Loadout → Create Default Catalog** makes `Assets/Chopper/Data/Loadout` with 4 chassis (Scout, Gunship, Assault, Lifter) and 23 modules from section 5.
- Left: apply `LoadoutStats` + weapon modules at server spawn, loadout in the GoInGame request / matchmaker ticket, chassis prefabs with sockets, hangar UI.

**Known follow-ups**
- Cinemachine 2 → 3 upgrade of the old camera prefabs (or drop them; the new rig does not use Cinemachine).
- Export `.blend` models to `.fbx` so the project does not need Blender.
- Pin `com.coplaydev.unity-mcp` to a version instead of `#main`.
- Old scenes (`Multiplayer`, `Training`, `FreeForAll`, `TeamDeathMatch`, `SampleScene`) have missing legacy scripts; delete or rebuild during later phases.

## 8. Phased roadmap

| Phase | Work | Result |
|---|---|---|
| **0. Upgrade** | ~~Upgrade to 6.3, package updates, API fixes, remove Multiplay~~ (done in `v2`). Left: 6.3 → 6.6; add Entities/N4E/Physics; remove NGO + ParrelSync; add VContainer (+ MessagePipe), assembly definitions, .editorconfig | Project opens on 6.6 and runs offline |
| **1. ECS core** | Chopper physics + input in Netcode for Entities; predicted movement; interpolated remotes; presentation GameObjects; HUD/camera bridge | 2+ choppers fly smoothly at 150 ms ping |
| **2. Combat** | Hitscan with lag compensation; predicted projectiles; server health/death/respawn; hit confirm; VFX from events | FFA playable on new netcode |
| **3. Services** | Steam sign-in; Sessions party; Matchmaker queues; Edgegap allocator; backfill; reconnect | One-click Play to a hosted match |
| **4. Customisation** | 4 chassis, module data, weight budget, cyberpunk wireframe hangar with socket focus camera, server validation | Loadouts in matches |
| **5. Progression** | Cloud Code grants, Cloud Save, levels, mastery, unlocks, leaderboards, Steam achievements | Persistent accounts |
| **6. Modes** | TDM, then Capture the Nuke; bots | All 3 modes |
| **UI (runs across phases)** | UI Toolkit theme + controls (Phase 0–1), hangar/party/progression (3–5), menus (3), HUD (2) | No UGUI left |
| **7. Polish/scale** | Load tests with thin clients; bandwidth budget; anti-cheat checks; region tuning | Launch-ready |

## Critical files

- Replace: `Networking/ClientNetworkTransform.cs`, `PlayerInteraction/PlayerAttackManager.cs`, `Weapons/AmmoEffectServer.cs`, `Weapons/AmmoEffectClient.cs`, `Weapons/ServerParticleHandler.cs`, `Interactions/NetworkHealth.cs`, `Server/ServerStartup.cs`, `Networking/MatchmakerClient.cs`.
- Port to ECS systems: `PlayerInteraction/MovementController.cs`, `VehicleStabiliser.cs`, `Abilities/Dash.cs`, `PlayerInteraction/Networking/PlayerManager.cs`, `Networking/ServerSpawnManager.cs`, `Networking/ScoreManager.cs`, `GameLogic/NetworkMatchStateMachine.cs` + states.
- Reuse data: `Weapons/ScriptableObjects/*`, `GameLogic/ScriptableObjects/GameMode.cs`, `MatchData.cs`, `TeamData.cs`, `PlayerData.cs`, `Enums/Chassis.cs`, `Enums/Team.cs`.
- Keep (presentation): `Audio/**`, `Cameras/**`, `Effects/**`.
- Rewrite in UI Toolkit: `UI/**`, `UI/HUD/**` (keep logic only where useful, e.g. `Utilities/Speed.cs`).
- New: `Packages/manifest.json` changes; Cloud Code modules (allocator, match results); Steam bootstrap.

## Verification

- **Each phase:** Multiplayer Play Mode with 4 players + Network Simulator (100–250 ms, 2% loss). Check smooth flight, no rubber-banding.
- **Hit detection test:** shooter at 200 ms ping hits a target that dodges; log client-seen tick vs server rewind tick; hits must match within capsule radius. Add PlayMode tests for lag-comp tick alignment (Unity advise this).
- **Bandwidth:** Netcode Profiler; target < 30 KB/s per client at 12 players in full combat.
- **Load:** 24 thin clients/bots on one server build; server tick stays ≥ 60 Hz.
- **Services:** end-to-end Steam sign-in → party → queue → Edgegap server → match → results written by Cloud Code → XP shown in menu.
- **Cheat checks:** modified client sends fake health/fire rate/loadout → server rejects.
