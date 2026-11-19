# Chopper death: falling wrecks, break-up, blast push

Owner requests, 19 Nov 2026: a killed chopper does not always explode at once. The engines cut, it spins down and
explodes when it hits a surface; the kill cam follows it. How it dies depends on how and where it was hit, its speed
and height. On the explosion it breaks apart (plan section 7, "Death break-up", 29 Oct). Explosions also push live
choppers.

Before this work: the server destroyed a chopper on the tick it died; clients saw it vanish (no explosion), and the
camera stopped where it was.

## Rules

- Server authoritative. The kill, the score and the kill feed happen at the moment of death (as before), not at the
  explosion. The respawn timer also starts at death.
- The death style is chosen once, on the server, from the killing blow, and replicated. All clients see the same fall.
- Every wreck explodes when it hits a surface, or after `MaxSeconds` (6 s), so it never floats.

## Death styles (`DeathStyles.Pick`, pure, tested)

| Killing blow | Style | Motion |
|---|---|---|
| Crash, landing, topple, out of bounds | Explode | at once |
| Explosion with overkill >= 3 (blow / health left) | Explode | air burst |
| Less than 8 m above the ground | Explode | no room to fall |
| Burn or corrosion | Engine spiral | slow yaw and roll, 70 % lift, long glide |
| Hit on the tail (back 35 % of the body) | Tail spin | fast flat spin round the mast, rising, 55 % lift |
| Hit on top (rotor) | Rotor drop | no lift, slow tumble |
| Hit on the nose | Nose dive | pitches nose down, 25 % lift, keeps its speed |
| Other bullet / explosion hits | Engine spiral | |

Hit zones use the hit point in the chopper's own space, scaled by its collider box, so every chassis size works.
The wreck keeps its velocity. Spin direction comes from the hit side.

## Parts

- `Wreck` (ghost component on the chopper prefab): style, spin sign, former owner, age. `Style == None` = alive.
- `DeathBlow` (server only): kind, local hit point, overkill. Written by `ApplyDamageSystem` on the killing hit.
- `WreckFlightSystem` (predicted, client and server): spin and lift of the style; `ChopperFlightSystem` skips wrecks.
- `WreckSystem` (server): turns a dead chopper into a wreck (owner cleared, so it is nobody's local chopper), explodes
  it on contact or time-out (`WreckExplodedRpc` to all), destroys it. `ChopperRespawnSystem` no longer destroys dead
  choppers that can be wrecks; it only starts the timer.
- Client: smoke and fire on the falling wreck, the explosion on `WreckExplodedRpc`, and the kill cam: while you have
  no chopper, the camera chases your wreck (stable yaw, not the spin) and holds on the explosion.

## Steps

1. Death styles, wreck flight, explosion on impact, kill cam. (this step)
2. Visual break-up: on the explosion the modules and body parts fly off as client debris (pooled), with fire and
   smoke trails. They keep the wreck's velocity and are pushed out from the blast; a tail hit can drop the tail boom
   during the fall.
3. Server physics parts and blast push: large parts (body sections, rotors) become short-lived server physics ghosts
   that hit other choppers (damage decided by the server; kill credit to the player who destroyed that chopper). The
   same blast maths pushes live choppers away from rocket, missile and wreck explosions (owner, 19 Nov).
