# First playtest — checklist

2–4 people, Windows client builds, Edgegap server, city map. Fill in one sheet per session.

## Before

- [ ] Client built from current code (Setup window → "Build Windows client (test)"), zipped, sent.
- [ ] Server built from the same commit and pushed as a new Edgegap version; allocator `Version` set to it.
- [ ] Allocator up (development or production) and the client points at it.
- [ ] Each tester: `AirwingNitro.exe -loadLog -logFile playtest.log`. Collect the logs after.
- [ ] Note the commit, map and number of players.

## Judge (score 1–5, plus one line why)

**Balance**
- Weapons: time to kill, magazine sizes, which weapon everyone picks.
- Missiles, flares and locks against people (not bots): fair? Too strong?
- Chassis: is one clearly best?

**Feel**
- Turn weight per chassis; hover and speed control.
- Hits on other players: do they register where you aimed? Hit sounds and markers clear?
- Crashes into buildings and the ground: fair or frustrating?

**Hitches and network**
- Rubber-banding, snaps, stalls (write the time).
- Joining, the load screen, leaving, a lost connection.
- Server load: `[Load]` lines (`Tools/Edgegap/fetch-logs.ps1`): ticks/s, sim % of the tick budget, worst frame, hitches. The server has 0.5 vCPU.

**Map flow**
- Where do fights happen? Dead areas nobody visits?
- Spawns: too close to enemies or walls? (Known: all players spawn near one point.)
- Can you find enemies? Too much or too little cover?

## After

- [ ] Top 3 problems, top 3 good things.
- [ ] Decide: bots next, or Capture the Nuke (owner).
- [ ] Stop the Edgegap deployment by hand if version 0.1.0 is still in use (it has no idle stop).
