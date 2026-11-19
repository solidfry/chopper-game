# Cyberpunk Audio Processor tool

Editor window for batch-processing audio clips into a gritty near-future sound.

Open with **Tools → Audio → Cyberpunk Audio Processor**.

Code: `Assets/Chopper/Editor/AudioEffects/` (assembly `Chopper.Editor`).

## Why it exists

The game needs a "2070" sound for guns and explosions: gritty, but believable as real
hardware. The tool lets you hear a chain, save it, and export a whole folder. Use it
instead of writing a one-off audio script.

## Files

| File | Role |
|---|---|
| `CyberpunkAudioProcessorWindow.cs` | Editor window: layout, file list, transport, export, presets |
| `AudioEffectProcessor.cs` | All DSP. Pure C#, no Audio Mixer. Stereo. |
| `AudioEffectChain.cs` | ScriptableObject of all effect settings (save/load a chain) |
| `EditorAudioPreview.cs` | Reflection wrapper over Unity's internal `UnityEditor.AudioUtil` |
| `EditorTransportIcons.cs` | Unity builtin transport icons, with vector fallback |

## Use it

1. **Source** folder — default `Assets/ThirdParty/Audio/Gun & Explosion Sounds` (git-ignored, see below).
2. Tick clips in the left list (filter box at the top; All / None / Invert).
3. Pick a **preset** or set the effects by hand.
4. Press **Play** to audition. **Compare original** A/Bs the
   raw clip against the processed one and re-auditions on toggle.
5. **Export selected** / **Export all** writes `<name>_cyberpunk.wav` to the Export folder.

Chains save and load as `.asset` files (Save / Load buttons).

## Effect chain order

1. EQ low shelf → 2. EQ peaking → 3. EQ high shelf
→ 4. distortion → 5. saturation → 6. bitcrush
→ 7. compression → 8. chorus → 9. flanger → 10. wah-wah
→ 11. delay → 12. reverb → 13. pitch shift
→ peak normalise → soft limiter.

Output is stereo at the source sample rate. Pitch shift pads/trims back to the
original length, so no samples are discarded.

## Presets

`2070` (first) — futuristic but believable. Grit from controlled saturation, not
full bitcrush; the "future" comes from a lifted ~5 kHz band, a tight servo-like
transient and a short ping-pong slap plus a close damped metal room. No wah (dates
it), no pitch shift (breaks believability). Measured +1.9% top-end lift vs source,
highest crest factor of all presets (retains punch).

Others: `Gritty`, `Dark`, `Industrial`, `Metallic`, `Submerged`, `Radio`.

## Two things that will bite you

**1. Processed previews go through a temp asset.**
A clip made with `AudioClip.Create` reports `IsPlaying() == true` on the editor
preview graph, but **no audio reaches the device**. So processed playback writes
`Assets/Chopper/TempPreview/CyberpunkPreview.wav`, imports it, and plays the imported
asset. Original (bypass) mode plays the real asset directly. Do not "simplify" this
back to playing the in-memory clip — it will silently lose sound.

**2. `AudioUtil` is an internal Unity API.**
`EditorAudioPreview` resolves every method by reflection and returns `false` if a
future Unity renames them. The window then shows a warning rather than breaking.
Signatures verified on Unity 6.6: `PlayPreviewClip(AudioClip, int, bool)`,
`StopAllPreviewClips()`, `IsPreviewClipPlaying()`, `GetPreviewClipPosition()`,
`SetPreviewClipSamplePosition(AudioClip, int)`, `LoopPreviewClip(bool)`.

## Source assets are git-ignored

The bundles this tool reads are **not in the repository** (too large):

- `Assets/ThirdParty/Audio/Gun & Explosion Sounds` — 230 MB, 540 clips (the default source)
- `Assets/ThirdParty/Audio/Ultimate SFX Bundle - HD Remaster` — 1.0 GB
- `Assets/ThirdParty/Audio/Colossal Game Music Collection` — 653 MB
- `Assets/ThirdParty/Audio/Total Music Collection` — 710 MB

On a fresh clone the file list is empty. Restore the folders, then press **Reload**.

`Assets/Chopper/TempPreview/` is also ignored — scratch output, rewritten each preview.

## Adding an effect

1. Add the fields to `AudioEffectChain` (with `[Range]`).
2. Add a `static void XxxStage(...)` in `AudioEffectProcessor` and call it in
   `ApplyEffects` at the right point in the chain.
3. Add a `DrawXxxSection()` in the window and place it in `DrawEffectGrid`
   (column A or B). The enable toggle goes on the accordion header, not inside it.
4. Set it fully in every preset, or it inherits the ScriptableObject default.

Keep stages operating on separate left/right arrays — interleaved processing is what
broke the original chorus/flanger/delay implementation.
