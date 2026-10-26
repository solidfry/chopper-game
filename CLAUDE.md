# Chopper game — notes for Claude

- Plan and progress: `Docs/MultiplayerOverhaulPlan.md`. Work on branch `v2`. New code lives in `Assets/Chopper` (see its README for assemblies).
- Stack: Unity 6.6, Netcode for Entities, Unity Physics, URP, UI Toolkit, VContainer, MessagePipe, UniTask.
- **Think systemically** (plan section 1c): one config root, data-driven definitions, one presentation bridge, one simulation→presentation event path, shared building blocks, one editor setup/validation window. Extend an existing mechanism before adding a new one.
- No singletons, no static mutable state, no new UGUI. Server is authoritative; clients never write health, score or hits.
- Code is often written without the editor: say so, and list the editor steps the user must do.
- The user wants short replies in ASD-STE100 Simplified English, as dot points.
