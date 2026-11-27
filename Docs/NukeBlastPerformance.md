# Nuke blast performance

Tracks the performance work on the Capture the Nuke blast (`Presentation/Effects/NukeBlastPlayer`, `NukeBlastSystem`,
`Editor/Setup/NukeBlastAssets`, `Art/Shaders/NukeCloud.shader`). Owner asked 24 Nov 2026: "do those changes, track them
in the docs".

## What one blast costs (24 Nov 2026, after the changes below)

- About 40 particle systems (simulated by Unity on worker threads), up to about 9,000 particles at once.
- 25 mesh renderers (cap, stem, burn mark, 3 shock rings, 3 wind strokes, 16 ground puffs) with per-frame material values.
- 2 point lights (flash for 3 s, fire in the cap until it cools), 3 lens flares, 1 screen post-effect volume.
- Server: `NukeShockSystem` (Burst), about 3 s of work per blast.

Not profiled yet. Next: a Unity Profiler capture of one detonation in TrainingRange, near and far.

## Changes

| # | Change | Status | Where |
|---|---|---|---|
| 1 | **No hitch at detonation.** One blast is built hidden when a Capture the Nuke match starts (`NukeState` exists) or the bench opens; detonation moves and starts it, and the end puts it back (stopped, cleared) instead of destroying it. One screen volume for all blasts. A second blast at the same time builds another. | Done 24 Nov | `NukeBlastPlayer.Prewarm`, `TakeSpare`, `End`; `NukeBlastSystem` (prewarm), `EffectsBench` (prewarm) |
| 2 | **Less overdraw.** Fewer overlapping smoke and fire cards: ground billows burst 170 → 110 and 22 → 13 per s; ground fire 60 → 38 per s; rim smoke 28 → 18 per s; cap fire 45 → 30 per s. | Done 24 Nov | `NukeBlastAssets` (BaseClouds, RimSmoke, CapFire) |
| 3 | **Distance detail.** Past 800 m from the camera, every system emits half (rates and bursts). Most players see the blast from far away. | Done 24 Nov | `NukeBlastPlayer.Detail`, `FarDistance`, `FarDetail` |
| 4 | **Cheaper cloud shader.** Bake the 3D noise into a small 3D texture (sample once or twice, not 5 to 6 Fbm calls); cheaper path when small on screen. Part done: ground puffs switch off once dissolved (from 7 s). | Part done | `NukeCloud.shader`; `NukeBlastPlayer.Animate` |
| 5 | **Particle limits by quality.** Scale `maxParticles` and emission with the graphics quality (for example x0.5 on Low). | Todo | `NukeBlastPlayer.Detail` can take the quality share |
| 6 | **Fire light.** Range 630 → 405 m (`NukeFlashRange` x 0.45); switched off when the fire has cooled (heat below 0.33). | Done 24 Nov | `NukeBlastPlayer.MakeFireLight`, `Lights` |
| 7 | **Fewer material updates.** Send heat and fade only when they change, or one shared value for the puffs. | Todo (small gain) | `NukeBlastPlayer.Set` |
| 8 | Shock push as a scheduled job. | Not worth it | `NukeShockSystem` is Burst and tiny |

## Measured

- Bench, 24 Nov: `Nuke()` with the pooled blast took 4.7 ms on the first use (includes the flash light, screen quad and
  flares) and **0.5 ms** on reuse. The old path instantiated the whole prefab in that frame (not measured before the change).
- Reuse checked: after a blast ends the instance is back in the pool (1 child), and the next blast plays the same.

## Open

- Profiler capture in a match (CPU and GPU, near and far) to confirm 2 and 3 and to size 4 and 5.
- Opus code review of the nuke commits for performance: done (owner, 27 Nov).
