# E:\Sounds — manifest

Source: `E:\Sounds` (owner, 22-24 Nov 2026). 4,342 files, 32 GB, three groups. Not in the repo (too big).
Every audio file, with length, sample rate, channels and bit depth read from the WAV header, is in
[`ESoundsFiles.csv`](ESoundsFiles.csv) (4,279 rows; MP3 rows have no length). Remake it with
`python Docs/Sounds/scan_sounds.py E:/Sounds Docs/Sounds/ESoundsFiles.csv` (it only reads file headers).
Nobody has listened to the SFX yet. Fit notes come from file names and lengths only.

| Group | Packs | Audio | What it is |
|---|---|---|---|
| Dark Fantasy Studio | 18 | 744 WAV + 744 MP3 (each track in both) | Music albums (dark synth, cyberpunk, orchestral) and an alien voice pack |
| Khron Sounds | 5 | 1,535 WAV | Already described in [`../KhronSoundsManifest.md`](../KhronSoundsManifest.md) (moved here from Downloads) |
| SFX | 14 | 1,256 WAV + 504 MP3 | Krotos libraries (guns, foley, mechanical, ambiences) and three Dark Fantasy SFX packs |

Licences: the Krotos packs come with `SFX/Krotos Sound Library EULA.pdf`; most Dark Fantasy packs have a licence PDF
in their folder. Read them before a public release.

## In the game now (24 Nov)

The owner picked **Neon God** and **Chaos Logic Chaos: The Butterfly Effect** for game music. The 10 full
(not "seamless") MP3 tracks of each are copied to `Assets/Chopper/Audio/Music/NeonGod` and `.../ButterflyEffect`
(Git LFS, streaming import like the other music) and added to the match playlist in
`Assets/Chopper/Data/Audio/MusicLibrary.asset` (22 → 42 tracks, shuffled). The "(seamless)" versions are loop
cuts; use them if a track must loop (for example a hangar or results loop).

| Neon God (56 min) | Length | The Butterfly Effect (48 min) | Length |
|---|---|---|---|
| Complexe mind | 3:12 | Backstabbed | 1:12 |
| Connected | 3:22 | Defense Master System | 1:50 |
| Cyber | 2:35 | Dystopia | 2:38 |
| Cyborg | 2:08 | Headshot | 1:00 |
| Dark web | 3:06 | Keep the distance | 2:02 |
| MDK | 2:41 | Me, myself, my enemy | 3:37 |
| Neon god | 2:22 | Powershift | 3:44 |
| Probs | 2:48 | Redemption | 2:14 |
| The heist | 2:59 | The bad guys | 3:17 |
| Them droids | 3:16 | The truth | 3:32 |

## Dark Fantasy Studio — music

All 44.1 kHz stereo WAV (mostly 24-bit) with an MP3 copy. Most tracks also come as a "(seamless)" loop.

| Album | Tracks (WAV) | Length range | Total | Fit for our game |
|---|---|---|---|---|
| **Neon god** | 20 | 2:06 – 3:22 | 56 min | **In the game.** Cyberpunk synth. |
| **Chaos logic chaos: the butterfly effect** | 20 | 0:46 – 3:44 | 48 min | **In the game.** Action synth. |
| Dark skies and other disasters | 48 | 0:32 – 3:32 | 104 min | Has an "Action part" folder: candidate combat tracks |
| Darkventures | 33 | 0:43 – 3:54 | 78 min | Candidate |
| The monster that lies within | 38 | 0:32 – 3:24 | 80 min | Dark; candidate for menus |
| Shadows | 18 | 2:04 – 5:47 | 63 min | Candidate |
| Underworld | 20 | 1:04 – 4:00 | 57 min | Candidate |
| Unia | 20 | 1:52 – 3:47 | 54 min | Candidate |
| The 29th planet | 20 | 1:55 – 3:16 | 52 min | One track of this album is already in the playlist (from `Assets/Sound/Music`) |
| Chuck kick ass | 26 | 0:57 – 3:54 | 52 min | Candidate (action) |
| Once upon a nightmare | 20 | 0:26 – 3:46 | 46 min | Horror; low fit |
| Mindhunter | 22 | 1:20 – 3:44 | 44 min | Thriller; menus or results |
| Aurora | 9 | 2:56 – 6:09 | 40 min | Ambient; hangar candidate |
| Forever and a day | 21 | 2:18 – 9:23 | 77 min | Long ambient; low fit |
| A journey to origins | 24 | 0:28 – 2:53 | 31 min | Fantasy; low fit |
| The lab | 22 | 0:50 – 2:02 | 28 min | Short sci-fi cues: menus, results |
| Chronicles of the illusion world | 29 | 0:30 – 1:15 | 20 min | Fantasy; low fit |
| PIXEL Faster stronger harder | 9 | 1:08 – 2:32 | 17 min | Chiptune; low fit |

## Dark Fantasy Studio — voices and SFX

| Pack | WAV | Length | Content | Fit for our game |
|---|---|---|---|---|
| Alien voices | 164 (82 mono + 82 stereo copies) | 1.0 – 5.5 s | 140 alien voice lines, 24 alien hit and death | Low; maybe a drone or announcer layer after processing |
| Interface (in `SFX`) | 200 (100 mono + 100 stereo) | 0.5 – 2.1 s | 178 UI sounds, 22 "combined" | **High**: menu clicks, hangar fit, lock and kill confirm. Compare with Khron Interface |
| Lightgun (in `SFX`) | 242 (121 + 121) | 0.4 – 2.8 s | 142 shots, 60 combined shots, 40 reloads | **High**: laser and energy weapon shots, reload for the rearm pad |
| Electric spell (in `SFX`) | 62 (31 + 31) | 1.9 – 4.1 s | Electric zaps and crackles | Medium: EMP, shield hit, damaged-system sparks |

## SFX — Krotos libraries

| Pack | WAV | Format | Length | Content | Fit for our game |
|---|---|---|---|---|---|
| Real-world Weapons | 200 | 96 kHz / 24 / stereo | 1.0 – 8.1 s | AK47, M4, M16, HK416, MINIMI, GPMG, Dragunov, Beretta, CZ75, MAC10, shotguns (pump, double barrel, Winchester), silenced shots, flamethrower; near / mid / far perspectives, short to long bursts | **High**: the GPMG and MINIMI bursts suit the chopper gun; far perspectives for other players' fire |
| Gun Foley | 178 | 192 kHz / 24 / mono | 0.2 – 5.9 s | Handling and reload for 12 guns | Medium: rearm and weapon swap clicks |
| Mechanical | 80 | 192 kHz / 24 / stereo | 0.5 – 19.8 s | Blowtorch, chain doors, hydraulic and pneumatic ramps, air guns, grinders, drills, impact gun, welding | **High** for the hangar: fitting modules, doors, the repair drone |
| Vehicle Ambience | 28 | 48 / 96 kHz | 12 s – 3:40 | BMW i3 and Tesla interior / exterior drives | Low (car interiors) |
| Dundee City Ambience | 16 | 96 kHz | 3 s – 4:51 | Wet street traffic loops, bus / truck / car passbys | Medium: city map bed and traffic passbys |
| California Ambiences | 9 | 96 kHz | 1:05 – 9:36 | LA night traffic with cicadas and sirens, beach, motel | Medium: the "LA night traffic, sirens" loops as a city bed |
| Office Ambience | 43 | 96 kHz | 0.2 s – 11:23 | Office room tone, elevator, doors, keyboard, microwave | Low; elevator and doors for menus at most |
| Train Travel | 6 | 96 kHz | 0:27 – 16:34 | Station and ride ambiences | Low |
| Air Travel | 3 | 96 kHz | 4:11 – 7:28 | Airplane cabin, airport crowd | Low |
| Brighton Ambiences | 4 | 96 kHz | 1:20 – 4:32 | Beach, pier, pub | Low |
| Japan Ambiences | 2 | 96 kHz | 0:56 – 1:19 | Graveyard at sunset, Tokyo subway | Low |

## Next steps

1. Owner: play a match and listen to the new music in the shuffle. Remove tracks that do not fit from the playlist.
2. Gun pass (on the todo list, "per-weapon sounds"): compare Real-world Weapons GPMG / MINIMI bursts, Khron Plasma
   Rifle and Dark Fantasy Lightgun for each weapon. Run picks through `Tools/Audio/Cyberpunk Audio Processor` into
   `Assets/Chopper/Audio/SFX`.
3. City ambience bed: Dundee wet traffic + LA night traffic and sirens, mixed low, per city map.
4. Hangar: Mechanical (hydraulic ramp, chain door, impact gun) for fitting modules and the door.
