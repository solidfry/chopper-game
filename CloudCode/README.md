# Cloud Code modules

## ChopperAllocator (matchmaker → Edgegap)

Unity Matchmaker calls this C# module to start a match server on Edgegap (plan section 4). Our own code, written after
Unity's example allocators (github.com/Unity-Technologies/matchmaker-hosting-providers).

- `Matchmaker_AllocateServer`: starts an Edgegap deployment of app `airwingnitro`, version `0.1.1` near the players
  (their public IPs come in the ticket custom data, key `player_ip`; the game client fills it).
- `Matchmaker_PollAllocation`: reports the server's public IP and the external port of `gameport` when it is ready.
- The Edgegap **API token** comes from the Unity secret `edgegap1` (Dashboard → Administration → Secrets).

New server version: build the Linux server (Setup window), then `./Tools/Edgegap/release.ps1 -Version <v>` pushes the
image, makes the Edgegap app version (a copy of the current one; `-InPlace <name>` re-points an existing version when the
account's version limit is reached) and sets `Version` in `Project/EdgegapAllocator.cs`. Then deploy the module again.

### Deploy

Unity Editor: **Services → Deployment**, select `Assets/Chopper/CloudCode/ChopperAllocator.ccmr`, **Deploy Selected**
(needs the .NET SDK; the editor builds the module). Or the UGS CLI: `ugs deploy CloudCode/ChopperAllocator`.

### Matchmaker queue

The queue is a file too: `Assets/Chopper/Matchmaker/Default.mmq` (queue `Default`, pool `ffa`: one team of 1-8
players, Cloud Code hosting with this module) and `Matchmaker.mme` (matchmaker on, default queue `Default`). Deploy
both from **Services → Deployment** like the module; deploying replaces the queue in the dashboard. The files are
camelCase (the editor reader needs `"type": "CloudCode"` in lower case). The game joins the queue named in
`HangarSettings` (`Default`) in the environment from `ServiceEnvironment` (`development` in the editor).

Build locally: `dotnet build -c Release` in `CloudCode/ChopperAllocator`.

## Player progress (in the same module)

`Project/ProgressStore.cs` keeps each player's progression record in Cloud Save, **protected player data**, key
`profile` (the game's `PlayerProfile` as JSON text). Plan section 6a-1, step 4. **Written without a compiler: build
the module before the first deploy.**

- `Progress_Read(playerId)` and `Progress_Write(playerId, matchId, profile)`: the game server only. A call from a
  player has a player id and is refused; the game server calls with a service account, which has none.
- `Progress_Mine()`: a signed-in player reads their own record (the game does this after sign-in).
- The game server works out awards and XP with the game's own rules (`ServerProgressSystem` in the Unity project), so
  the module holds no game rules.

### Dashboard steps (once per environment)

1. Administration > Service Accounts: make a service account for the game server. Give it the project roles it needs
   to run Cloud Code modules and to read and write Cloud Save player data. Make a key: note the key id and secret key.
2. Administration > Secrets: add `ugs_key_id` and `ugs_secret_key` with those two values. The allocator puts them (hidden),
   with the project id and environment id, on each Edgegap deployment as `UGS_KEY_ID`, `UGS_SECRET_KEY`,
   `UGS_PROJECT_ID`, `UGS_ENVIRONMENT_ID`. Without the secrets a match runs without server-side progress.
3. Deploy the module again (it has the new functions and the allocator change).

### Test on one PC (no Edgegap)

Set the four `UGS_...` environment variables by hand, start a server build, then start a client with
`-join 127.0.0.1:PORT -account <player id>` (the client log shows the id: "Signed in as ... (id)"). The server log
shows `[Progress]` lines. The record is in the dashboard: Cloud Save > Player Data > the player > protected.

