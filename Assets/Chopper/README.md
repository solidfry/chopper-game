# Chopper (new code)

All new code goes here. Legacy code in `Assets/Scripts` stays in `Assembly-CSharp` until it is ported, then it is deleted.

| Assembly | Contains | May reference |
|---|---|---|
| `Chopper.Simulation` | ECS components, systems, bakers (Netcode for Entities). Runs on client and server. | Unity packages only |
| `Chopper.Services` | Plain C# services: sign-in, matchmaking, save, Steam, audio. Interfaces + implementations. | Unity packages only |
| `Chopper.Presentation` | Bridges ECS → GameObjects: VFX, audio, camera, HUD view models. | Simulation, Services |
| `Chopper.UI` | UI Toolkit screens, custom controls, USS/UXML. Reads view models only. | Presentation, Services |
| `Chopper.Tests.EditMode` | Edit-mode tests. | All of the above |

Rules (see `Docs/MultiplayerOverhaulPlan.md`, section 1b):
- Dependencies point one way. Simulation never references Presentation or UI.
- No `Singleton<T>`, no `static event`, no static mutable state.
- New code never references `Assets/Scripts` (Assembly-CSharp cannot be referenced anyway).
