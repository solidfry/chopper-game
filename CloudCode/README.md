# Cloud Code modules

## ChopperAllocator (matchmaker → Edgegap)

Unity Matchmaker calls this C# module to start a match server on Edgegap (plan section 4). Our own code, written after
Unity's example allocators (github.com/Unity-Technologies/matchmaker-hosting-providers).

- `Matchmaker_AllocateServer`: starts an Edgegap deployment of app `airwingnitro`, version `0.1.1` near the players
  (their public IPs come in the ticket custom data, key `player_ip`; the game client fills it).
- `Matchmaker_PollAllocation`: reports the server's public IP and the external port of `gameport` when it is ready.
- The Edgegap **API token** comes from the Unity secret `edgegap1` (Dashboard → Administration → Secrets).

New server version: change `Version` in `Project/EdgegapAllocator.cs` and deploy again.

### Deploy

Unity Editor: **Services → Deployment**, select `Assets/Chopper/CloudCode/ChopperAllocator.ccmr`, **Deploy Selected**
(needs the .NET SDK; the editor builds the module). Or the UGS CLI: `ugs deploy CloudCode/ChopperAllocator`.

### Matchmaker queue

Dashboard → Matchmaker → Queues → `production` → its pool → **Hosting: Cloud Code**:

| Field | Value |
|---|---|
| Module name | `ChopperAllocator` |
| Allocate function | `Matchmaker_AllocateServer` |
| Poll function | `Matchmaker_PollAllocation` |

The game joins the queue named in `HangarSettings` (`production`).

Build locally: `dotnet build -c Release` in `CloudCode/ChopperAllocator`.
