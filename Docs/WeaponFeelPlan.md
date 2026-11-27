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

## Risks / open questions

- Voice count: many guns at 1,000+ rpm. Keep the fire sound a loop per gun, not a one-shot per round.
- Ghost change (rocket module id) needs a rebuild of server and client before online tests.
- Railgun / Pulse Laser are hitscan: a beam look needs the hit point (already known in `DrawShots`).
- Asset sources: confirm the packs in `ThirdParty` have bomb / mine models; else PartForge parts.
- Order if time is short: 1 -> 2 -> 5 -> 3 -> 4.
