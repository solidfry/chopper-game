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
