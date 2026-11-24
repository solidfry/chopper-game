# Khron Studio sound packs — manifest

Source: `E:\Sounds\Khron Sounds` (owner purchase, 19 Nov 2026; moved from Downloads, see `Sounds/ESoundsManifest.md`). 1,535 WAV files, 8.7 GB, 5 packs. Not in the repo.
Pack sections were written by the local model (qwen3.8-27b) from file names and lengths only. Nobody has listened to them yet.
The category counts in the Interface pack are estimates. The real file count is in the list below.

| Pack | WAV files |
|---|---|
| Demolition Aftermath | 299 |
| Futuristic Weapons Vol 1 | 205 |
| Interdimensional Interface Vol 1 | 574 |
| Fireworks Explosions Vol 2 | 316 |
| Metal Core Alliance 2 | 141 |

## Review (Claude): best upgrade targets

1. **Explosions and chopper deaths** (big gain): Fireworks *Designed* (`EXPLDsgn_Spaceship Explosion In The Sky`, landmine) plus close-range firework blasts as the crack layer, and Demolition *Metal* fractures as the debris tail. Trim to 2-4 s; the sources are long.
2. **Gun fire**: we have one gun set (`Gun 14_x`). Weapons Vol 1 *Plasma Rifle* / *Assault Rifle* single shots fit the minigun or cannon. Per-weapon sounds are already on the todo list.
3. **Missile and rocket launch**: Fireworks *Launches & Whistles* (`FRWKRec_Firework Launch`) as the launch whoosh layer.
4. **Building and ground hits**: Demolition *Rocks* and *Glass* (city towers) for `ConcreteCrash`, `ConcreteHit` and `GroundCrash`.
5. **UI and HUD**: Interface pack beeps and clicks for menus, lock tone and kill confirm. The robot voice lines (e.g. "New enemy detected", "System critical") could be an announcer.
6. **Warnings**: keep the remade lock and missile warnings (17-18 Nov, the owner tuned them); Interface alarms only as an option to compare.
7. **Hangar**: Metal Core doors and mecha latches for the hangar loadout screen (fitting a module, the door opening).

Next step: listen to a short list per target, then run the picks through `Tools/Audio/Cyberpunk Audio Processor` (trim, level) into `Assets/Chopper/Audio/SFX`.

## Khron Studio - Demolition Aftermath
299 high-resolution (192kHz) stereo destruction sounds covering structural collapses, material fractures, and debris impacts.

| Category | Count | Typical length | Example files | Fit for our game |
| :--- | :--- | :--- | :--- | :--- |
| Building | 30 | 5.0s – 10.3s | `DESTRClpse_Building Collapse Type A 01_KRST_NONE.wav` | High. Adds weight to large structure destruction in cyberpunk cityscapes. |
| Car | 17 | 2.0s – 4.0s | `DESTRCrsh_Car Crash Type A 01_KRST_NONE.wav` | Medium. Improves vehicle impact variety for ground targets or collisions. |
| Glass | 52 | 1.3s – 8.2s | `GLASBrk_Glass Crack Heavy 01_KRST_NONE.wav` | High. Essential for shattering skyscraper windows and neon-lit facades. |
| Metal | 54 | 3.4s – 11.6s | `METLCrsh_Colossal Metal Fracture Type A 01_KRST_NONE.wav` | High. Replaces generic metal hits with heavy, resonant structural failures. |
| Rocks | 63 | 1.2s – 9.2s | `ROCKCrsh_Heavy Rock Burst Type A_KRST_NONE.wav` | High. Upgrades concrete/stone debris sounds for explosions and impacts. |
| Wood | 83 | 1.4s – 9.0s | `WOODCrsh_Medium Or High Collapse Wood Type A 01_KRST_NONE.wav` | Low. Less relevant for a high-tech cyberpunk setting, but usable for older districts. |

### Upgrade candidates
*   `SFX/ConcreteCrash/Rock Debris Large 01_2070.wav` → `Rocks\Heavy Rock Burst Type A\ROCKCrsh_Heavy Rock Burst Type A_KRST_NONE.wav`
*   `SFX/ConcreteHit/Rock Debris Small 01_2070.wav` → `Rocks\Break Rock Type A\ROCKCrsh_Break Rock Type A 01_KRST_NONE.wav`
*   `SFX/DamageTaken/Metal Debris 01_2070.wav` → `Metal\Twisted Metal Collapse Type A\METLCrsh_Twisted Metal Collapse Type A 01_KRST_NONE.wav`
*   `SFX/MetalCrash/Car Crash 01_2070.wav` → `Car\Car Crash Type A\DESTRCrsh_Car Crash Type A 01_KRST_NONE.wav`
*   `SFX/MetalHit/Metal Small Impact 1_1_2070.wav` → `Metal\Seismic Steel Strain Type A\METLCrsh_Seismic Steel Strain Type A 01_KRST_NONE.wav`
*   `SFX/GroundCrash/Rock Debris Large 08_2070.wav` → `Rocks\Stone Collapse_Debris Type A\ROCKCrsh_Stone Collapse Debris Type A 01_KRST_NONE.wav`

## Khron Studio - Futuristic Weapons Vol 1 (UCS System)
205 sci-fi weapon SFX (48kHz/2ch) covering assault rifles, blasters, lasers, plasma, pistols, shotguns, snipers, and grenades with full equip/reload/shot cycles.

| Category | Count | Typical length | Example files | Fit for our game |
| :--- | :--- | :--- | :--- | :--- |
| Assault Rifle | 22 | 0.4s – 2.3s | `SCIWeap_Assault_Rifle_Single_Shot _01.wav`, `SCIWeap_Assault_Rifle_Reload_Designed_01.wav` | High. Replaces generic gun fire with distinct sci-fi assault rifle layers. |
| Blaster Gun | 24 | 0.8s – 3.5s | `LASRGun_Blaster_Single_Shot_01.wav`, `LASRGun_Blaster_Burst_Medium_Shot_01.wav` | High. Core cyberpunk weapon sound; adds variety to current single gun loop. |
| Laser Gun | 16 | 0.5s – 1.6s | `LASRGun_Laser_Gun_Single_Shot_01.wav`, `LASRGun_Laser_Gun_Equip_01.wav` | High. Distinct from blaster; good for secondary weapons or enemy turrets. |
| Plasma Rifle | 12 | 0.6s – 4.3s | `LASRGun_Plasma_Rifle_Single_Shot_01.wav`, `LASRGun_Plasma_Rifle_Burst_Shot.wav` | High. Heavy, low-end impact fits helicopter combat scale. |
| Pistol | 18 | 0.2s – 1.5s | `SCIWeap_Pistol_Single_Shot_01.wav`, `SCIWeap_Pistol_Reload_Designed_01.wav` | Medium. Useful for ground troops or close-quarters helicopter boarding. |
| Shotgun | 18 | 0.2s – 3.3s | `GUNShotg_Shotgun_Single_Shot_01.wav`, `GUNShotg_Shotgun_Reload_Bullets_01.wav` | Medium. Close-range burst potential for helicopter door guns. |
| Sniper Rifle | 20 | 0.4s – 1.3s | `GUNRif_Sniper_Rifle_Single_Shot _01.wav`, `GUNRif_Sniper_Rifle_Zoom_01.wav` | Low-Medium. Long-range precision shots; zoom SFX useful for UI/ADS. |
| Grenade | 25 | 0.3s – 2.5s | `SCIMisc_Grenade_Countdown_01.wav`, `EXPLDsgn_Grenade_Explosion_Designed_01.wav` | High. Adds fragmentation explosion variety to current rock/metal debris. |

### Upgrade candidates
*   `SFX/GunFire/Gun 14_1_2070.wav` → Replace with `Khron Studio - Futuristic Weapons Vol 1 (UCS System)\Blaster_Gun\Blaster_Gun_Shot\Blaster_Gun_Single_Shot\LASRGun_Blaster_Single_Shot_01.wav` (and variants 02-04) for a more distinct cyberpunk primary weapon.
*   `SFX/GunFire/Gun 14_2_2070.wav` → Replace with `Khron Studio - Futuristic Weapons Vol 1 (UCS System)\Assault_Rifle\Assault_Rifle_Shot\Assault_Rifle_Single_Shot\SCIWeap_Assault_Rifle_Single_Shot _01.wav` (and variants 02-07) to add a rapid-fire assault rifle option.
*   `SFX/GunFire/Gun 14_3_2070.wav` → Replace with `Khron Studio - Futuristic Weapons Vol 1 (UCS System)\Plasma_Rifle\Plasma_Rifle_Shot\Plasma_Rifle_single_shot\LASRGun_Plasma_Rifle_Single_Shot_01.wav` (and variants 02-04) for a heavy, high-impact weapon sound.
*   `SFX/GunFire/Gun 14_4_2070.wav` → Replace with `Khron Studio - Futuristic Weapons Vol 1 (UCS System)\Laser_Gun\Laser_Gun_Shot\Laser_Gun_Single_Shot\LASRGun_Laser_Gun_Single_Shot_01.wav` (and variants 02-04) to introduce a distinct laser weapon profile.
*   `SFX/GunFire/Gun 14_5_2070.wav` → Replace with `Khron Studio - Futuristic Weapons Vol 1 (UCS System)\Shotgun\Shotgun_Shot\Shotgun_Single_Shot\GUNShotg_Shotgun_Single_Shot_01.wav` (and variants 02-04) for close-range burst capability.
*   `SFX/Flares/FlareBurst_01.wav` → Replace with `Khron Studio - Futuristic Weapons Vol 1 (UCS System)\Grenade\Grenade_Explosion\EXPLDsgn_Grenade_Explosion_01.wav` (and variants 02-03) to add a sharper, more energetic explosion layer to flare bursts.
*   `SFX/Flares/FlareBurst_02.wav` → Replace with `Khron Studio - Futuristic Weapons Vol 1 (UCS System)\Grenade\Grenade_Explosion_Designed\EXPLDsgn_Grenade_Explosion_Designed_01.wav` (and variants 02-10) for a more designed, layered explosion sound.
*   `SFX/Flares/FlareBurst_03.wav` → Replace with `Khron Studio - Futuristic Weapons Vol 1 (UCS System)\Grenade\Grenade_Explosion_Designed\EXPLDsgn_Grenade_Explosion_Designed_05.wav` (and variants 06-10) to add variation to the flare explosion palette.

## Khron Studio- Interdimensional Interface Vol 1 (UCS System)
Sci-fi UI, robotic voice, and ambient tech loops at 48kHz/2ch, suitable for HUD, menus, and cockpit systems.

| Category | Count | Typical length | Example files | Fit for our game |
| :--- | :--- | :--- | :--- | :--- |
| UI Beeps & Clicks | 120 | 0.1s – 1.5s | `UIBeep_Computer_Beep_01.wav`, `UIClick_Short_Click_Menu_Button_01.wav`, `UIBeep_Tiny_Beep_01.wav` | High. Replaces generic UI clicks; adds cyberpunk HUD feedback. |
| Alarms & Warnings | 30 | 0.2s – 6.5s | `SCIAlrm_Alarm_Loop_01.wav`, `SCIAlrm_Alarm_Single_Shot_01.wav` | High. Direct upgrade for missile/lock warnings. |
| Robotic Voice (VOX) | 150 | 0.7s – 3.8s | `ROBTVox_New_enemy_detected_Talking_Robot_Type_A.wav`, `ROBTVox_System_Critical_Talking_Robot_Type_F.wav` | High. Adds AI comms for mission updates and system status. |
| Ambient Loops | 50 | 4.5s – 21.0s | `AMBTech_Machine_Room_Background_Loop_01.wav`, `SCICmpt_Computer_Room_Background_Sequence_Beep_Loop_01.wav` | Medium. Cockpit/hangar ambience; not for open-world city. |
| Holograms & FX | 30 | 0.3s – 2.7s | `SCIEnrg_Hologram_01.wav`, `UIMisc_Get_Object_01.wav`, `WHSH_Whoosh_01.wav` | Medium. HUD transitions, target acquisition, menu opens. |

### Upgrade candidates
*   `SFX/Warnings/LockWarning_2070.wav` → `Khron Studio- Interdimensional Interface Vol 1 (UCS System)\Alarm\Alarm_Single_Shot\SCIAlrm_Alarm_Single_Shot_01.wav`
*   `SFX/Warnings/LockWarning_Beep.wav` → `Khron Studio- Interdimensional Interface Vol 1 (UCS System)\Alarm\Alarm_Single_Shot\SCIAlrm_Alarm_Single_Shot_07.wav`
*   `SFX/Warnings/MissileWarning_Alarm.wav` → `Khron Studio- Interdimensional Interface Vol 1 (UCS System)\Alarm\Alarm_Loop\SCIAlrm_Alarm_Loop_01.wav`

## Khron_Studio_Fireworks_Explosions_Vol2_UCS
High-resolution field recordings and designed explosions covering close-to-long range firework blasts, launches, and ambient sequences.

| Category | Count | Typical length | Example files (2-3 names) | Fit for our game |
| :--- | :--- | :--- | :--- | :--- |
| Designed Explosions | 3 | 14–44s | `EXPLDsgn_Hand Grenade Detonation_KRST_NONE.wav`, `EXPLDsgn_Spaceship Explosion In The Sky_KRST_NONE.wav` | High-impact cinematic hits; "Spaceship" fits helicopter/aircraft downing. |
| Close Range Explosions | 30 | 4.5–8.8s | `FRWKComr_Firework Explosion At Close Range 01_KRST_EM258.wav`, `FRWKComr_Firework Explosion At Close Range 05_KRST_EM258.wav` | Intense, low-frequency blasts for nearby missile impacts or flares. |
| Medium Range Explosions | 46 | 4.4–10.5s | `FRWKComr_Firework Explosion At Medium Range 01_KRST_EM258.wav`, `FRWKComr_Firework Explosion At Medium Range 20_KRST_EM258.wav` | Standard combat explosion layer; good balance of crack and rumble. |
| Long Range Explosions | 27 | 5.4–9.7s | `FRWKComr_Firework Explosion At Long Range 01_KRST_EM258.wav`, `FRWKComr_Fireworks Explosions Sequence 01_KRST_EM258.wav` | Distant city background chaos; adds depth to cyberpunk skyline. |
| Launches & Whistles | 18 | 2.9–17.6s | `FRWKRec_Firework Launch 01_KRST_X8.wav`, `FRWKRec_Whistling Fireworks Ambience 01_KRST_X8.wav` | Missile launch whooshes and incoming projectile warnings. |
| Sequences & Ambience | 192 | 5.8–137.9s | `FRWKRec_Fireworks Explosion Ambience Blast 02_KRST_MKH 8050.wav`, `FRWKRec_Heavy Fireworks Finale Finale Ambience Type B_KRST_X8.wav` | Loopable background battle ambience for intense city zones. |

### Upgrade candidates
*   **SFX/Flares/Flare Explosion 01_2070.wav** → Replace with `Recordings\Firework Explosion At Close Range\FRWKComr_Firework Explosion At Close Range 01_KRST_EM258.wav` (sharper, more detailed burst).
*   **SFX/Flares/FlareBurst_01.wav** → Replace with `Recordings\Small Ground Explosion Fireworks\FRWKRec_Small Ground Explosion Fireworks 01_KRST_EM258.wav` (more organic ground-level flare pop).
*   **SFX/ConcreteCrash/Rock Debris Large 01_2070.wav** → Enhance with `Designed\EXPLDsgn_Outdoor Landmine Explosion_KRST_NONE.wav` (layered for heavier structural impact).
*   **SFX/Warnings/MissileWarning_Alarm.wav** → Supplement with `Recordings\Whistling Fireworks Ambience\FRWKRec_Whistling Fireworks Ambience 01_KRST_X8.wav` (adds physical whoosh to the audio alarm).

## Khron_Studio_Metal_Core_Alliance_2_UCS
High-resolution (96k/192k) sci-fi mechanical, door, and robotic sound design library focused on heavy machinery, cybernetic interfaces, and futuristic structural movements.

| Category | Count | Typical length | Example files (2-3 names) | Fit for our game |
| :--- | :--- | :--- | :--- | :--- |
| **Building Blocks / Doors & Gates** | 38 | 5s - 42s | `DOORTech_Scifi Door Sliding Blades Open 01_KRST_NONE.wav`, `DOORMetl_Large Gate Final Impact 01_KRST_NONE.wav`, `DOORTech_Massive Gate Pneumatic Piston Activation 01_KRST_NONE.wav` | Excellent for hangar doors, cyberpunk building entrances, and vehicle bay access. High-quality stereo imaging. |
| **Building Blocks / Mecha & Machinery** | 42 | 3s - 30s | `MACHTech_Giant Mecha Platform Impact 01_KRST_NONE.wav`, `MACHTech_Heavy Metal Latch Release 01_KRST_NONE.wav`, `MACHTech_Mecha Steel Part Lock 01_KRST_NONE.wav` | Ideal for helicopter rotor maintenance, heavy landing gear deployment, and industrial city background ambience. |
| **Building Blocks / Sci-Fi & UI** | 25 | 3s - 27s | `SCIMisc_Micro Device Ui Laser Beep 01_KRST_NONE.wav`, `SCIMisc_Droid High Tech Glitch 01_KRST_NONE.wav`, `SCIMech_Ring Scanner Movement_KRST_NONE.wav` | Perfect for HUD interactions, missile lock-on confirmations, and cybernetic interface feedback. |
| **Sequences** | 36 | 6s - 61s | `DOORTech_Massive Gate Pneumatic Piston Activation 02_KRST_NONE.wav`, `MACHTech_Giant Mecha Assembly Sequence_KRST_NONE.wav`, `SCIMisc_Nano Suit Microchip Interconnect 01_KRST_NONE.wav` | Pre-mixed complex events for loading screens, major structural shifts, or system boot-up sequences. |

### Upgrade candidates
*   **SFX/Warnings/LockWarning_Beep.wav** → Replace with `Building Blocks\Micro Machine Modular Deploy\SCIMisc_Micro Device Ui Laser Beep 01_KRST_NONE.wav` (Cleaner, more futuristic UI beep for missile lock).
*   **SFX/Warnings/LockWarning_2070.wav** → Replace with `Building Blocks\Security Android Servo Scan\SCIMisc_Droid High Tech Glitch 01_KRST_NONE.wav` (Adds cyberpunk glitch texture to warning alerts).
*   **SFX/MetalHit/Metal Small Impact 1_1_2070.wav** → Replace with `Building Blocks\Heavy Machinery Clamp\MACHTech_Heavy Metal Latch Release 01_KRST_NONE.wav` (More distinct mechanical "clunk" for helicopter parts or city infrastructure hits).
*   **SFX/DamageTaken/Metal Debris 01_2070.wav** → Replace with `Building Blocks\Mecha Limb Plate Docking Slam\MACHTech_Mecha Component Growl 01_KRST_NONE.wav` (Adds low-end resonance for heavy damage feedback).

