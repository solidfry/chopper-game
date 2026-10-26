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
        private const string MatchMusic = "Assets/Sound/Music/3- Dark Fantasy Studio- Demons from Mars (seamless).mp3";

        private const string SciFi = "Assets/AssetBundles/Archanor/Sci-Fi Arsenal/Sci-Fi Effects/Sound";
        private const string Explosions = "Assets/Sound/Explosions/TomWinandySFX_Explosions Volume I_CloseExplosion_";

        public const string MixerPath = "Assets/Audio/AudioMix/Mix.mixer";
        public const string MatchMusicPath = MatchMusic;

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
                localRotor: Slot(library.RotorLocal, () => Cue("RotorLocal", Clips("Assets/Sound/inside-of-a-helicopter-54404.ogg"), ambient, 0.5f, blend: 0.2f, voices: 1)),
                remoteRotor: Slot(library.RotorRemote, () => Cue("RotorRemote", Clips("Assets/Sound/inside-of-a-helicopter-54404.ogg"), effects, 0.9f, blend: 1f, voices: 16)),
                dashCue: Slot(library.Dash, () => Cue("Dash", Clips(SciFi + "/Impact/shoot_energy.wav"), effects, 0.6f, blend: 0.5f, voices: 2)),
                // The old game's gun: a close explosion pitched up (it was on the BulletRed prefab). Gritty and kinetic.
                gun: Slot(library.GunFire, () => Cue("GunFire", Clips(Explosions + "11.wav"), effects, 0.82f, blend: 1f, voices: 6, pitch: 1.45f)),
                hit: Slot(library.HitConfirm, () => Cue("HitConfirm", Clips("Assets/Sound/UI/Switch/Switch (1).wav", "Assets/Sound/UI/Switch/Switch (2).wav",
                    "Assets/Sound/UI/Switch/Switch (4).wav"), ui, 0.5f, blend: 0f, voices: 2, cooldown: 0.06f)),
                kill: Slot(library.KillConfirm, () => Cue("KillConfirm", Clips("Assets/Sound/UI/Switch/Switch (5).wav", "Assets/Sound/UI/Switch/Switch (6).wav"),
                    ui, 0.9f, blend: 0f, voices: 1, pitch: 0.8f)),
                damage: Slot(library.DamageTaken, () => Cue("DamageTaken", Clips(Explosions + "01.wav", Explosions + "02.wav", Explosions + "03.wav"),
                    effects, 0.25f, blend: 0f, voices: 2, cooldown: 0.15f, pitch: 1.4f)),
                alarm: Slot(library.OutOfBoundsAlarm, () => Cue("OutOfBoundsAlarm", Clips(SciFi + "/Loops/loop_energy.wav"), ui, 0.4f, blend: 0f, voices: 1)),
                end: Slot(library.MatchEnd, () => Cue("MatchEnd", Clips("Assets/Sound/UI/273736__squashy555__computer-startup.wav"), ui, 0.8f, blend: 0f, voices: 1)),
                click: Slot(library.UIClick, () => Cue("UIClick", ClipsIn("Assets/Sound/UI/KeyboardSounds"), ui, 0.7f, blend: 0f, voices: 2)),
                navigate: Slot(library.UINavigate, () => Cue("UINavigate", ClipsIn("Assets/Sound/UI/ComputerWirrSounds"), ui, 0.4f, blend: 0f, voices: 2, cooldown: 0.05f)));

            if (created) AssetDatabase.CreateAsset(library, LibraryPath);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        private static SoundCue Slot(SoundCue current, System.Func<SoundCue> make) => current != null ? current : make();

        private static SoundCue Cue(string name, AudioClip[] clips, AudioMixerGroup group, float volume, float blend, int voices,
            float cooldown = 0f, float pitch = 1f)
        {
            var path = $"{Folder}/Cue_{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
            if (existing != null) return existing;

            var cue = ScriptableObject.CreateInstance<SoundCue>();
            cue.Initialise(clips, group, volume, blend, voices, cooldown, pitch);
            AssetDatabase.CreateAsset(cue, path);
            return cue;
        }

        private static AudioClip[] Clips(params string[] paths)
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

        private static AudioClip[] ClipsIn(string folder) => AssetDatabase
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
