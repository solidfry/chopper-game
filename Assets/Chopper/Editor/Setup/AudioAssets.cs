using System.Collections.Generic;
using System.Linq;
using Chopper.Presentation.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Sound cues and library made from the project's existing sounds. Creates them if missing; swap any clip on the
    /// cue assets afterwards.
    /// </summary>
    internal static class AudioAssets
    {
        private const string Folder = "Assets/Chopper/Data/Audio";
        private const string LibraryPath = Folder + "/SoundLibrary.asset";
        private const string MusicPath = Folder + "/MusicLibrary.asset";
        private const string DarkSynth = "Assets/Sound/Music/";
        private const string Packs = "Assets/Total Music Collection/Mega Game Collection/";
        /// <summary>The old main menu's track: the game's identity music (owner, 29 Oct).</summary>
        private const string IdentityTrack = Packs + "Heavy Riffs/Heavy Riff 1.mp3";
        /// <summary>
        /// Match playlist: metal from the music packs (the old match scene's "Crazy In My Mind" first) blended with the
        /// owner's dark synth picks in Sound/Music.
        /// </summary>
        private static readonly string[] MatchPlaylist =
        {
            Packs + "Real Rock Music Pack/MP3/Crazy In My Mind.mp3",
            DarkSynth + "3- Dark Fantasy Studio- Demons from Mars (seamless).mp3",
            Packs + "Real Rock Music Pack/MP3/Heavy Battle.mp3",
            DarkSynth + "1- Dark Fantasy Studio- Behind space (seamless).mp3",
            Packs + "Real Rock Music Pack/MP3/Drive Angry.mp3",
            DarkSynth + "2- Dark Fantasy Studio- Planet hell (seamless).mp3",
            Packs + "Energy Hard Rock Pack Remasters/Cyber Rock (Remastered).wav",
            DarkSynth + "8- Dark Fantasy Studio- You are better off dead (seamless).mp3",
            Packs + "Real Rock Music Pack/MP3/Attack of Madness.mp3",
            DarkSynth + "City.ogg",
            Packs + "Heavy Riffs 5/Industrial MIX.mp3",
            DarkSynth + "7- Dark Fantasy Studio- The 29th planet (seamless).mp3",
        };

        private const string SciFi = "Assets/AssetBundles/Archanor/Sci-Fi Arsenal/Sci-Fi Effects/Sound";
        private const string Explosions = "Assets/Sound/Explosions/TomWinandySFX_Explosions Volume I_CloseExplosion_";
        private const string DistantExplosions = "Assets/Sound/Explosions/TomWinandySFX_Explosions Volume I_DistantExplosion_";
        internal const string SciFiGuns = "Assets/AssetBundles/Sci-Fi Gun Sounds";

        public const string MixerPath = "Assets/Audio/AudioMix/Mix.mixer";

        /// <summary>
        /// The music library: identity track and match playlist. Created if missing (an existing one keeps its picks).
        /// Music clips stream from disk, so long tracks do not sit in memory.
        /// </summary>
        public static MusicLibrary EnsureMusic()
        {
            var library = AssetDatabase.LoadAssetAtPath<MusicLibrary>(MusicPath);
            if (library == null)
            {
                System.IO.Directory.CreateDirectory(Folder);
                library = ScriptableObject.CreateInstance<MusicLibrary>();
                library.Initialise(AssetDatabase.LoadAssetAtPath<AudioClip>(IdentityTrack),
                    MatchPlaylist.Select(AssetDatabase.LoadAssetAtPath<AudioClip>).Where(c => c != null).ToArray());
                AssetDatabase.CreateAsset(library, MusicPath);
            }

            foreach (var clip in library.Match.Append(library.Menu).Where(c => c != null)) Stream(clip);
            return library;
        }

        private static void Stream(AudioClip clip)
        {
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) is not AudioImporter importer) return;
            var settings = importer.defaultSampleSettings;
            if (settings.loadType == AudioClipLoadType.Streaming) return;
            settings.loadType = AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        public static AudioMixer LoadMixer()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null) Debug.LogError($"Audio setup: no mixer at {MixerPath}.");
            return mixer;
        }

        /// <summary>
        /// Creates the library if missing, and fills any empty cue slot (delete a cue asset to get its default back).
        /// Never changes a cue that exists.
        /// </summary>
        public static SoundLibrary Ensure(AudioMixer mixer)
        {
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);
            if (mixer == null) return library;
            System.IO.Directory.CreateDirectory(Folder);

            var created = library == null;
            if (created) library = ScriptableObject.CreateInstance<SoundLibrary>();

            var effects = Group(mixer, "Effects");
            var ui = Group(mixer, "UI");
            var ambient = Group(mixer, "Ambient");

            library.Initialise(
                library.Mixer != null ? library.Mixer : mixer,
                library.MusicGroup != null ? library.MusicGroup : Group(mixer, "Music"),
                // Rotor_Interior_Loop: inside-of-a-helicopter with its level raised 17 dB (the original is at -33 dB) and a seamless loop.
                localRotor: Slot(library.RotorLocal, () => Cue("RotorLocal", Clips("Assets/Sound/Rotor_Interior_Loop.wav"), ambient, 0.6f, blend: 0.2f, voices: 1, muffle: CueMuffle.Cabin)),
                remoteRotor: Slot(library.RotorRemote, () => Cue("RotorRemote", Clips("Assets/Sound/Rotor_Interior_Loop.wav"), effects, 1f, blend: 1f, voices: 16, muffle: CueMuffle.Outside)),
                // The old game's gun: a close explosion pitched up (it was on the BulletRed prefab). Gritty and kinetic.
                gun: Slot(library.GunFire, () => Cue("GunFire", Clips(Explosions + "11.wav"), effects, 0.82f, blend: 1f, voices: 6, pitch: 1.45f, muffle: CueMuffle.Outside)),
                hit: Slot(library.HitConfirm, () => Cue("HitConfirm", Clips("Assets/Sound/UI/Switch/Switch (1).wav", "Assets/Sound/UI/Switch/Switch (2).wav",
                    "Assets/Sound/UI/Switch/Switch (4).wav"), ui, 0.5f, blend: 0f, voices: 2, cooldown: 0.06f)),
                kill: Slot(library.KillConfirm, () => Cue("KillConfirm", Clips("Assets/Sound/UI/Switch/Switch (5).wav", "Assets/Sound/UI/Switch/Switch (6).wav"),
                    ui, 0.9f, blend: 0f, voices: 1, pitch: 0.8f)),
                damage: Slot(library.DamageTaken, () => Cue("DamageTaken", Clips(Explosions + "01.wav", Explosions + "02.wav", Explosions + "03.wav"),
                    effects, 0.25f, blend: 0f, voices: 2, cooldown: 0.15f, pitch: 1.4f, muffle: CueMuffle.Cabin)),
                alarm: Slot(library.OutOfBoundsAlarm, () => Cue("OutOfBoundsAlarm", Clips(SciFi + "/Loops/loop_energy.wav"), ui, 0.4f, blend: 0f, voices: 1)),
                end: Slot(library.MatchEnd, () => Cue("MatchEnd", Clips("Assets/Sound/UI/273736__squashy555__computer-startup.wav"), ui, 0.8f, blend: 0f, voices: 1)),
                click: Slot(library.UIClick, () => Cue("UIClick", ClipsIn("Assets/Sound/UI/KeyboardSounds"), ui, 0.7f, blend: 0f, voices: 2)),
                navigate: Slot(library.UINavigate, () => Cue("UINavigate", ClipsIn("Assets/Sound/UI/ComputerWirrSounds"), ui, 0.4f, blend: 0f, voices: 2, cooldown: 0.05f)));

            // Targeting tones (placeholders from the effects pack; swap on the cue assets).
            library.InitialiseTargeting(
                Slot(library.LockTone, () => Cue("LockTone", Clips(SciFi + "/Loops/loop_laser.wav"), ui, 0.35f, blend: 0f, voices: 1, pitch: 0.8f)),
                Slot(library.LockedTone, () => Cue("LockedTone", Clips(SciFi + "/Loops/loop_laser2.wav"), ui, 0.45f, blend: 0f, voices: 1, pitch: 1.3f)),
                Slot(library.LockWarning, () => Cue("LockWarning", Clips(SciFi + "/Loops/loop_energy.wav"), ui, 0.4f, blend: 0f, voices: 1, pitch: 1.2f)),
                Slot(library.MissileWarning, () => Cue("MissileWarning", Clips(SciFi + "/Loops/loop_lightning.wav"), ui, 0.55f, blend: 0f, voices: 1)),
                Slot(library.Flares, () => Cue("Flares", Clips(SciFi + "/Impact/shoot_fireball.wav"), effects, 0.7f, blend: 1f, voices: 4, muffle: CueMuffle.Outside)));

            // Boost: deep, rolling distant explosions (and one sci-fi blast) for the kick; the loudest, bassiest rocket loop,
            // pitched well down, for the roar. Chosen by low-frequency content; the close explosions crackle (tinny).
            library.InitialiseBoost(
                Slot(library.BoostStart, () => Cue("BoostStart", Clips(DistantExplosions + "15.wav", DistantExplosions + "16.wav",
                    DistantExplosions + "06.wav", SciFiGuns + "/Sci Fi Explosion 06.wav"), effects, 0.6f, blend: 0.3f, voices: 3, pitch: 0.8f, muffle: CueMuffle.Cabin)),
                Slot(library.BoostLoop, () => Cue("BoostLoop", Clips(SciFi + "/Loops/loop_rocket2.wav"), effects, 0.5f, blend: 0.3f, voices: 8,
                    pitch: 0.5f, muffle: CueMuffle.Cabin)));

            // Hangar (placeholders): mechanical reload clunks for fit and remove, short laser charges for focus and scan.
            library.InitialiseHangar(
                Slot(library.ModuleFit, () => Cue("ModuleFit", Clips(SciFiGuns + "/Weapon 01 Reload 1.wav", SciFiGuns + "/Weapon 07 Reload 1.wav"),
                    ui, 0.6f, blend: 0f, voices: 2, cooldown: 0.08f)),
                Slot(library.ModuleRemove, () => Cue("ModuleRemove", Clips(SciFiGuns + "/Weapon 02 Reload 1.wav"), ui, 0.5f, blend: 0f, voices: 2,
                    cooldown: 0.08f, pitch: 0.8f)),
                Slot(library.SlotFocus, () => Cue("SlotFocus", Clips(SciFiGuns + "/Weapon 05 Load (Laser).wav"), ui, 0.3f, blend: 0f, voices: 2,
                    cooldown: 0.1f, pitch: 1.3f)),
                Slot(library.ChassisScan, () => Cue("ChassisScan", Clips(SciFiGuns + "/Weapon 08 Reload 1 (Laser).wav"), ui, 0.5f, blend: 0f,
                    voices: 1, cooldown: 0.2f, pitch: 0.85f)));

            // Field repair (placeholders): the laser weapon's loop pitched down as a servo hum; a laser charge when full;
            // the laser's stop sound when a hit cuts it off.
            library.InitialiseRepair(
                Slot(library.RepairLoop, () => Cue("RepairLoop", Clips(SciFiGuns + "/Weapon 05 Loop (Laser).wav"), effects, 0.35f, blend: 0f,
                    voices: 1, pitch: 0.6f)),
                Slot(library.RepairDone, () => Cue("RepairDone", Clips(SciFiGuns + "/Weapon 12 Load (Laser).wav"), ui, 0.5f, blend: 0f,
                    voices: 1, cooldown: 0.5f, pitch: 1.2f)),
                Slot(library.RepairInterrupted, () => Cue("RepairInterrupted", Clips(SciFiGuns + "/Weapon 12 Stop (Laser).wav"), ui, 0.6f,
                    blend: 0f, voices: 1, cooldown: 0.3f, pitch: 0.8f)));

            // Pilot ability (placeholders): an energy blast on use, a short switch when ready, crackling lightning when jammed.
            library.InitialiseAbility(
                Slot(library.AbilityActivate, () => Cue("AbilityActivate", Clips(SciFi + "/Impact/explosion_energy.wav"), ui, 0.5f, blend: 0f,
                    voices: 1, cooldown: 0.2f, pitch: 1.2f)),
                Slot(library.AbilityReady, () => Cue("AbilityReady", Clips("Assets/Sound/UI/Switch/Switch (7).wav"), ui, 0.6f, blend: 0f,
                    voices: 1, cooldown: 0.5f)),
                Slot(library.Jammed, () => Cue("Jammed", Clips(SciFi + "/Impact/explosion_lightning.wav"), ui, 0.55f, blend: 0f,
                    voices: 1, cooldown: 0.5f, pitch: 0.8f)));

            // Surfaces (placeholders made by Tools/Sound/make_impact_sounds.py): bullet hits and crashes on metal, concrete,
            // ground and water. 3D. Hits are short and many, so a short cooldown and a few voices; crashes louder and rarer.
            const string impacts = "Assets/Sound/Impacts/";
            SoundCue SurfaceCue(SoundCue current, string surface, string kind, int count, float volume, int voices, float cooldown) =>
                Slot(current, () => Cue($"{surface}{kind}",
                    Clips(Enumerable.Range(1, count).Select(n => $"{impacts}{surface}_{kind}_{n}.ogg").ToArray()),
                    effects, volume, blend: 1f, voices: voices, cooldown: cooldown, muffle: kind == "Hit" ? CueMuffle.Outside : CueMuffle.Cabin));
            library.InitialiseSurfaces(
                new[]
                {
                    SurfaceCue(library.SurfaceHitCue(0), "Metal", "Hit", 3, 0.55f, 6, 0.04f),
                    SurfaceCue(library.SurfaceHitCue(1), "Concrete", "Hit", 3, 0.6f, 6, 0.04f),
                    SurfaceCue(library.SurfaceHitCue(2), "Ground", "Hit", 3, 0.6f, 6, 0.04f),
                    SurfaceCue(library.SurfaceHitCue(3), "Water", "Hit", 2, 0.5f, 6, 0.04f),
                },
                new[]
                {
                    SurfaceCue(library.SurfaceCrashCue(0), "Metal", "Crash", 3, 1f, 4, 0.12f),
                    SurfaceCue(library.SurfaceCrashCue(1), "Concrete", "Crash", 3, 1f, 4, 0.12f),
                    SurfaceCue(library.SurfaceCrashCue(2), "Ground", "Crash", 3, 1f, 4, 0.12f),
                    SurfaceCue(library.SurfaceCrashCue(3), "Water", "Crash", 2, 1f, 4, 0.12f),
                });

            library.InitialiseAmbience(Slot(library.CityAmbience, () => Cue("CityAmbience",
                Clips("Assets/Sound/Ambient/City/city din at night (off big street).wav"), ambient, 0.18f, blend: 0f, voices: 1, muffle: CueMuffle.Outside)));

            if (library.VoiceGroup == null) library.SetVoiceGroup(ui);

            if (created) AssetDatabase.CreateAsset(library, LibraryPath);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        private static SoundCue Slot(SoundCue current, System.Func<SoundCue> make) => current != null ? current : make();

        internal static SoundCue Cue(string name, AudioClip[] clips, AudioMixerGroup group, float volume, float blend, int voices,
            float cooldown = 0f, float pitch = 1f, CueMuffle muffle = CueMuffle.None)
        {
            var path = $"{Folder}/Cue_{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
            if (existing != null) return existing;

            var cue = ScriptableObject.CreateInstance<SoundCue>();
            cue.Initialise(clips, group, volume, blend, voices, cooldown, pitch, muffle);
            AssetDatabase.CreateAsset(cue, path);
            return cue;
        }

        internal static AudioClip[] Clips(params string[] paths)
        {
            var clips = new List<AudioClip>();
            foreach (var path in paths)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) clips.Add(clip);
                else Debug.LogWarning($"Audio setup: no clip at {path}.");
            }
            return clips.ToArray();
        }

        internal static AudioClip[] ClipsIn(string folder) => AssetDatabase
            .FindAssets("t:AudioClip", new[] { folder })
            .Select(guid => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(clip => clip != null)
            .ToArray();

        public static AudioMixerGroup Group(AudioMixer mixer, string name)
        {
            var group = mixer.FindMatchingGroups(name).FirstOrDefault(g => g.name == name);
            if (group == null) Debug.LogWarning($"Audio setup: mixer has no '{name}' group.");
            return group;
        }
    }
}
