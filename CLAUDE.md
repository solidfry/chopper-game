# Chopper game — notes for Claude

- Start with `Docs/Handoff.md` (current state, open problems, next steps). Old work logs: `Docs/HandoffHistory.md` (read only when pointed there). Plan and progress: `Docs/MultiplayerOverhaulPlan.md`. Work on branch `v2`. New code lives in `Assets/Chopper` (see its README for assemblies).
- Stack: Unity 6.6, Netcode for Entities, Unity Physics, URP, UI Toolkit, VContainer, MessagePipe, UniTask.
- **Think systemically** (plan section 1c): one config root, data-driven definitions, one presentation bridge, one simulation→presentation event path, shared building blocks, one editor setup/validation window. Extend an existing mechanism before adding a new one.
- No singletons, no static mutable state, no new UGUI.
- Unity 6.6 deprecations: no managed components (`AddComponentObject`, class `IComponentData`) — use `UnityObjectRef<T>` in unmanaged components; use `FindAnyObjectByType`, not `FindFirstObjectByType`. Server is authoritative; clients never write health, score or hits.
- Code is often written without the editor: say so, and list the editor steps the user must do.
- **Audio tooling:** `Tools/Audio/Cyberpunk Audio Processor` (code in `Assets/Chopper/Editor/AudioEffects`, docs `Docs/AudioProcessorTool.md`). It batch-processes audio clips through a 13-stage effect chain with presets, an embedded preview transport and WAV export. Use it before adding a new audio-tweak script. Note: the large audio bundles it reads from are git-ignored (see "Source assets" below), so on a fresh clone the file list is empty until they are restored.
- The user wants short replies in ASD-STE100 Simplified English, as dot points.

## Source assets (git-ignored — not in the repo)

These folders are excluded from version control and must be obtained separately. A fresh clone has **no audio**.

| Folder | Size | Used for |
|---|---|---|
| `Assets/Gun & Explosion Sounds` | 230 MB, 540 clips | Default input for the cyberpunk audio tool |
| `Assets/Ultimate SFX Bundle - HD Remaster` | 1.0 GB | Available SFX library |
| `Assets/Colossal Game Music Collection` | 653 MB | Available music library |
| `Assets/Total Music Collection` | 710 MB | Available music library (untracked from the index, still on disk) |
| `Packages/com.occasoftware.buto` | 192 MB | Buto volumetric fog (embedded package). Optional: `Chopper.Presentation.Buto` compiles only with it (`BUTO_PRESENT`); Setup step 9 adds its render feature |

Music used by the game lives in `Assets/Chopper/Audio/Music` and is stored with **Git LFS** (run `git lfs install` before cloning). Processed SFX are in `Assets/Chopper/Audio/SFX` (plain git).

`Assets/Chopper/TempPreview/` is also ignored — it is scratch output from the audio tool, rewritten on each preview.
