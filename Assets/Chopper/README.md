# Chopper (new code)

All new code goes here. Legacy code in `Assets/Scripts` stays in `Assembly-CSharp` until it is ported, then it is deleted.

| Assembly | Contains | May reference |
|---|---|---|
| `Chopper.Simulation` | ECS components, systems, bakers (Netcode for Entities). Runs on client and server. | Unity packages only |
| `Chopper.Services` | Plain C# services: sign-in, matchmaking, save, Steam, audio. Interfaces + implementations. | Unity packages only |
| `Chopper.Services.Steam` | Steamworks.NET sign-in ticket. Compiles only when the Steamworks.NET package is installed (`CHOPPER_STEAM`). | Services |
| `Chopper.Presentation` | Bridges ECS → GameObjects: VFX, audio, camera, HUD view models. | Simulation, Services |
| `Chopper.UI` | UI Toolkit screens, custom controls, USS/UXML. Reads view models only. | Presentation, Services |
| `Chopper.App` | Composition root (VContainer `RootLifetimeScope`) and start-up flow. | Services, Services.Steam |
| `Chopper.Editor` | Editor tools and menu items (Chopper → UI → Add HUD To Open Scene, Chopper → Loadout → Create Default Catalog). | Simulation, Presentation, UI |
| `Chopper.Tests.EditMode` | Edit-mode tests. | All of the above |

Rules (see `Docs/MultiplayerOverhaulPlan.md`, section 1b):
- Dependencies point one way. Simulation never references Presentation or UI.
- No `Singleton<T>`, no `static event`, no static mutable state.
- New code never references `Assets/Scripts` (Assembly-CSharp cannot be referenced anyway).
