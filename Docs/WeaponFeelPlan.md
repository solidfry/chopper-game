# Weapon feel plan (owner, 27 Nov 2026)

Goal: every weapon looks, sounds and reads as its own thing, and the hangar shows its numbers. Polish only: no balance
changes (damage, rate, speed stay as they are; tuning is a later pass).

## Where we are (27 Nov)

- **Guns (8 noses):** M134 Minigun, Minigun Mk II, Heavy MG, Chain Gun, 20mm Autocannon, Rotary Cannon, Railgun,
  Pulse Laser. All share **one** tracer, muzzle flash, impact (`CombatEffectsSettings.gunTracer/gunMuzzleFlash/gunImpact`)
  and **one** fire sound (`SoundLibrary.GunFire`). `GunEffectsSystem` has no idea which gun fired:
  `HitscanWeaponConfig` (and the turret's `TurretConfig.Gun`) carry stats only, no module id.
- **Pylons (rockets, 9):** Light / Heavy / Flak Rockets, Rocket Pod (+ Mk II), Salvo Pod, Swarm Pod, Bombs, Air Mines.
  All share **one** rocket visual, muzzle flash and impact (`rocketProjectile/rocketMuzzleFlash/rocketImpact`). The
  `Rocket` component has no module id either. Bombs and mines fly the rocket look (with gravity / drag).
- **Guided (2):** Homing Missiles, Cluster Missiles: own missile visual, smoke column, launch / flight / blast cues.
- **Hangar:** stats rows are chopper-wide (hull, speed, turn, climb, boost, refill, weight). No weapon numbers.

## Design (plan 1c: data-driven, one presentation bridge, extend before adding)

- **`WeaponLook` asset (Presentation, new, one per weapon family, not per module):** tracer / projectile prefab,
  muzzle flash, impact (by surface where it matters), fire cue, tail / distant cue, flight loop cue, impact cue,
  tracer colour and width, flash size, camera kick (trauma per shot), shell-casing on/off. A family covers a module
  and its Mk tiers (Minigun + Mk II share one look).
- **`WeaponLookLibrary` (in `GameConfig`, like `MusicLibrary`):** maps module id -> `WeaponLook`, with a default look
  per kind (gun, rocket, bomb, mine) so a new module works before it has its own. Validated by Setup (every weapon
  module has a look or a kind default).
- **Simulation gets one field, nothing else:** `ushort ModuleId` on `HitscanWeaponConfig`, `TurretConfig.Gun` (turret
  module id) and `Rocket` (set at spawn from the pylon's config). Presentation looks it up. No presentation types in
  simulation. Check first whether these configs are baked on both sides or replicated: if a rocket ghost needs the id,
  it is one more ghost field -> **server + client rebuild** before online tests.
- Effects stay pooled in `EffectPool` (one pool per prefab already).

## Steps (each one: compile, EditMode tests, play in the editor, screenshot / listen check, commit)

### Step 1 - Plumbing (no visible change)
1. `ModuleId` on `HitscanWeaponConfig`, `TurretConfig.Gun`, `Rocket` (and the rocket's ghost if it is one); set where the
   loadout builds the weapon and where the rocket spawns.
2. `WeaponLook`, `WeaponLookLibrary`, `GameConfig.WeaponLooks`, Setup creates the library with kind defaults that point
   at today's prefabs and cues (so the game looks and sounds the same).
3. `GunEffectsSystem`, `RocketVisualSystem`, `MissileEffectsSystem` read the look per shot (fallback: kind default).
4. Tests: library lookup (module -> family -> kind default), validation messages.
- Check: play TrainingRange, fire every gun and pylon: same as before, console clean.

### Step 2 - Guns: one look and sound per family
Families (proposal; owner can merge): **Minigun** (M134, Mk II: fast buzz, thin tracers, every 3rd round lit),
**Heavy MG** (thump, fat tracer), **Chain Gun** (steady mid rate), **Autocannon** (20mm + Rotary: big flash,
flak-like impact puffs), **Railgun** (one beam line that fades, charge whine, crack), **Pulse Laser** (bolts, no
shells, zap). Sources: Sci-Fi Arsenal / Archanor packs in `ThirdParty` and the Gun & Explosion bundle through the
Cyberpunk Audio Processor (`Tools/Audio`), same chain as the current SFX. Tail cue: far shots play a distant layer.
- Fire sound per family: loop-based for fast guns (start, loop, tail) so 3,000 rpm does not stack 50 voices.
- Camera kick per family (small for minigun, larger for cannon).
- Check: Effects Bench (`EffectsBench`) per family, then a match; owner listens (plain more / less choices).

### Step 3 - Rockets, bombs, mines
- **Rockets:** light (thin trail, small blast), heavy (thick trail, big blast and debris), flak (burst cloud at fuse),
  pods / salvo / swarm (shared pod launch "ripple" sound, smaller trails so a salvo reads).
- **Bombs:** a bomb model (PartForge or kit), whistle as it falls, ground blast with dust ring and scorch.
- **Air mines:** a mine model with a blinking light while it drifts, arm beep, proximity flash.
- Impacts by surface (metal / concrete / ground) through the existing `PhysicsSurface` lookup used by guns.
- Check: bench + match; layered blasts to the effects quality bar (real parts, layered).

### Step 4 - Guided missiles
- Homing vs Cluster: Cluster splits (small bomblets visual at split), own launch sound. Missile look stays the base.
- Check: bench + match.

### Step 5 - Hangar weapon stats
- When a weapon slot is hovered or open: a weapon block in the detail panel: **DAMAGE** (per shot or per rocket, splash),
  **RATE** (rpm or s between), **RANGE / SPEED**, **MAGAZINE / RELOAD**, **HEAT** (guns with a heat limit), with bars
  against the best in that slot and the green / red delta vs the fitted one (same style as the chopper stat rows).
- Numbers come from the definitions (`HitscanWeaponDefinition`, `RocketWeaponDefinition`, `GuidedMissileDefinition`)
  through `HangarModel` (pure, tested).
- Check: hangar screenshots, tests for the numbers.

### Step 6 - Hand-over
- Owner listens and looks per family; tune with plain more / less choices.
- Handoff row, `Docs/WeaponFeel` notes in the plan file, commit. If a ghost changed: rebuild server and client.

## Progress (owner away 27 Nov: run by /loop; owner reviews on return)

- **Step 1 done** (`a740674a`). `ModuleId` on `HitscanWeaponConfig`, `PylonWeapon`, `RocketLaunchRpc`, `LocalRocketLaunch`
  (not on the server-only `Rocket`: the RPC carries it). `WeaponLook` / `WeaponLookLibrary`
  (`Data/Effects/WeaponLooks`), linked from `CombatEffectsSettings.WeaponLooks` (not a new `GameConfig` field: the
  effects settings already own the shared prefabs, so the fallback lives in one place). Every empty look slot uses the
  shared prefab or cue. Turrets and cluster bomblets send id 0 and get the kind default. Guided missiles use the
  guided default until step 4 (the missile ghost has no module id). Checked: 7 tests; in training, bots fired 116
  rounds and a test rocket flew, with 0 errors.
  **`RocketLaunchRpc` changed: rebuild the server and client before online tests.**
- **Step 2 done** (`a14bcc95`). Six gun families, each with its own tracer, flash, impact, fire sound and camera kick
  (own gun only): Minigun (M134 + Mk II, yellow), Heavy MG (red, deep thump), Chain Gun (orange, heaviest shot, strong
  kick), Autocannon (20mm + Rotary, fireball flash, red fire puffs), Railgun (blue bolt, crack, big kick), Pulse Laser
  (green bolts, zap). Gun sounds were copied from the git-ignored bundle to `Audio/SFX/Guns` (tracked); the laser and
  railgun use `ThirdParty/Sci-Fi Gun Sounds`. Bench: **G** (or "Next gun family") fires an 8-round burst of each family
  in turn. Screenshots: `Docs/WeaponFeel/gun_*.png`.
  - Not done, and why: the loop fire sound (start, loop, tail). The fastest gun is 1,500 rpm (25 shots a second), so
    the one-shot cues with voice caps (2 to 6) stay within limits. Railgun beam line: it uses a fast bolt (3,000 m/s)
    for now.
  - **Owner checks:** in the bench, press G six times and listen. In a match, try each nose gun. Answer with plain
    more / less per family (louder, deeper, more kick, different colour). Sounds were picked by measurement, not by
    ear: tone (bright or deep) and ring length.

- **Step 5 done** (`1fda8a9b`). When a weapon slot is open, the stats column shows `// <WEAPON NAME>` and rows below
  the chopper stats. These are the hovered option's numbers, or the fitted weapon's. Guns: DAMAGE, DPS, RATE (rpm),
  RANGE, HEAT (seconds of steady fire, or NO LIMIT). Rockets and missiles: DAMAGE (direct + splash), BLAST, LOCK
  (guided only), RATE (seconds between), MAGAZINE, RELOAD, SPEED. Each bar is measured against the best weapon in that
  slot; green / red shows the change from the fitted weapon. If the fitted weapon is of another kind, no change
  shows. The maths is pure (`WeaponStatRows`) with 4 tests. Screenshot: `Docs/WeaponFeel/hangar_weapon_stats.png`.
  - **Owner checks:** open NOSE and a pylon slot, hover the options, and check that the numbers and row order read
    well.

- **Step 3 done** (`1e41c121`). Pylon families:
  - **Light Rockets:** yellow rocket, yellow flash, small yellow blast.
  - **Heavy Rockets:** the standard rocket with a bigger fire blast.
  - **Flak Rockets:** purple rocket that bursts in a sphere.
  - **Bombs:** a real bomb model (PolygonMilitary jet bomb, 1.8 m). No launch flash. A whistle follows it as it falls
    and stops at impact.
  - **Air Mines:** a dark spiked sphere with a red light that blinks 2.5 times a second; a red sphere burst.
  - Pods (Rocket Pod, Mk II, Salvo, Swarm) keep the standard rocket.
  - New look switches: flight loop sound, "scale with blast" (off for the bomb and mine models), and "no muzzle
    flash". The prefabs (`Prefabs/Weapons/Bomb`, `AirMine`) are built by Setup.
  - Screenshots: `Docs/WeaponFeel/pylon_bomb.png`, `pylon_airmine.png`.
  - **Impacts by surface done 28 Nov (/loop):** a rocket, bomb or mine that hits a surface throws it up on top of its
    own blast (`SurfaceBlast`, pure, 3 tests; emitted by `DamageParticles.EmitSurfaceBlast`). Ground: a plume in the
    map's dust colour and dark clods that arc and fall. Concrete: grey dust, chips, a few sparks. Metal: a shower of
    sparks. Water: a white spray column and mist. Air bursts (fuse) throw nothing. The rocket hit test now classifies
    the surface (`Surfaces.Classify`, as guns). One particle set is now shared (`CombatEffects.Particles`): damage,
    crash dust and blasts share their budgets. New falling "Blast Spray" system. Bench key **T** cycles the four
    surfaces. Screenshots: `Docs/WeaponFeel/surface_*.png`. Client-only: no rebuild needed.
  - Not done: the mine arm beep; a ripple launch sound for pods.
  - **Owner checks:** fly with Bombs (listen to the whistle) and Air Mines (does the blink read at range?); fire Light,
    Heavy and Flak Rockets. Bench T: rocket on concrete, metal, ground, water (more / less dust, clods, sparks,
    spray?).

- **Step 4 done** (`9f81a6cf`). `GuidedMissile.ModuleId` (ghost field) and `ClusterSplitRpc.ModuleId`. The missile
  visual, launch flash, launch sound and flight loop come from the family look.
  - **Homing Missiles:** keep the guided default (red).
  - **Ground Seeker:** orange missile and flash.
  - **Cluster Missiles:** green carrier, its own heavier launch sound.
  - **Cluster split:** yellow glowing bomblets. One yellow fireball flash and one pop sound play per split (before,
    every bomblet showed a rocket launch flash). New look slots: bomblet, split flash, split cue.
  - Played: a test split in training (screenshot `Docs/WeaponFeel/cluster_bomblets.png`).
  - **Not seen in play:** a real guided missile with its new look; the bots did not fire one in the test window.
  - **Owner checks:** fire Homing, Ground Seeker and Cluster; watch the split.
- **Step 6 (hand-over):** Handoff row written. Waiting on the owner's review. **Rebuild the server and client** (RPCs
  and a ghost field changed).

## Risks / open questions

- Voice count: many guns at 1,000+ rpm. Keep the fire sound a loop per gun, not a one-shot per round.
- Ghost change (rocket module id) needs a rebuild of server and client before online tests.
- Railgun / Pulse Laser are hitscan: a beam look needs the hit point (already known in `DrawShots`).
- Asset sources: confirm the packs in `ThirdParty` have bomb / mine models; else PartForge parts.
- Order if time is short: 1 -> 2 -> 5 -> 3 -> 4.
