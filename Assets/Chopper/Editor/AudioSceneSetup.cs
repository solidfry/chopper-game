using System.Collections.Generic;
using System.Linq;
using Chopper.Presentation.Audio;
using Chopper.Presentation.Effects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

namespace Chopper.Editor
{
    /// <summary>
    /// One-click audio: creates the sound cues and library from the project's existing sounds (if missing), routes the
    /// combat effects to the Effects mixer group, and adds an Audio object (plus match music) to the open scene.
    /// Safe to run again. Swap any clip on the cue assets afterwards.
    /// </summary>
    internal static class AudioSceneSetup
    {
        private const string Folder = "Assets/Chopper/Data/Audio";
        private const string LibraryPath = Folder + "/SoundLibrary.asset";
        private const string MixerPath = "Assets/Audio/AudioMix/Mix.mixer";
        private const string EffectsSettingsPath = "Assets/Chopper/Data/Effects/CombatEffectsSettings.asset";
        private const string MatchMusic = "Assets/Sound/Music/3- Dark Fantasy Studio- Demons from Mars (seamless).mp3";

        private const string SciFi = "Assets/AssetBundles/Archanor/Sci-Fi Arsenal/Sci-Fi Effects/Sound";
        private const string Explosions = "Assets/Sound/Explosions/TomWinandySFX_Explosions Volume I_CloseExplosion_";

        [MenuItem("Chopper/Audio/Add Audio To Open Scene")]
        private static void AddToOpenScene()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null)
            {
                Debug.LogError($"Audio setup: no mixer at {MixerPath}.");
                return;
            }

            var library = GetOrCreateLibrary(mixer);
            RouteCombatEffects(mixer);

            var host = Object.FindFirstObjectByType<AudioHost>();
            if (host == null)
            {
                var go = new GameObject("Audio");
                Undo.RegisterCreatedObjectUndo(go, "Add Audio");
                host = Undo.AddComponent<AudioHost>(go);
            }
            Undo.RecordObject(host, "Configure Audio");
            host.SetLibrary(library);
            EditorUtility.SetDirty(host);

            var music = host.GetComponent<SceneMusic>();
            if (music == null)
            {
                music = Undo.AddComponent<SceneMusic>(host.gameObject);
                music.SetTrack(AssetDatabase.LoadAssetAtPath<AudioClip>(MatchMusic));
                EditorUtility.SetDirty(music);
            }

            EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
            Selection.activeGameObject = host.gameObject;
        }

        private static SoundLibrary GetOrCreateLibrary(AudioMixer mixer)
        {
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);
            if (library != null) return library;
            System.IO.Directory.CreateDirectory(Folder);

            var effects = Group(mixer, "Effects");
            var ui = Group(mixer, "UI");
            var ambient = Group(mixer, "Ambient");

            library = ScriptableObject.CreateInstance<SoundLibrary>();
            library.Initialise(
                mixer,
                Group(mixer, "Music"),
                localRotor: Cue("RotorLocal", Clips("Assets/Sound/inside-of-a-helicopter-54404.ogg"), ambient, 0.5f, blend: 0.2f, voices: 1),
                remoteRotor: Cue("RotorRemote", Clips("Assets/Sound/inside-of-a-helicopter-54404.ogg"), effects, 0.9f, blend: 1f, voices: 16),
                dashCue: Cue("Dash", Clips(SciFi + "/Impact/shoot_energy.wav"), effects, 0.6f, blend: 0.5f, voices: 2),
                gun: Cue("GunFire", Clips(SciFi + "/Impact/shoot_pulsegun.wav"), effects, 0.45f, blend: 1f, voices: 6),
                hit: Cue("HitConfirm", Clips("Assets/Sound/UI/Switch/Switch (1).wav", "Assets/Sound/UI/Switch/Switch (2).wav",
                    "Assets/Sound/UI/Switch/Switch (4).wav"), ui, 0.5f, blend: 0f, voices: 2, cooldown: 0.06f),
                kill: Cue("KillConfirm", Clips("Assets/Sound/UI/Switch/Switch (5).wav", "Assets/Sound/UI/Switch/Switch (6).wav"),
                    ui, 0.9f, blend: 0f, voices: 1, pitch: 0.8f),
                damage: Cue("DamageTaken", Clips(Explosions + "01.wav", Explosions + "02.wav", Explosions + "03.wav"),
                    effects, 0.25f, blend: 0f, voices: 2, cooldown: 0.15f, pitch: 1.4f),
                alarm: Cue("OutOfBoundsAlarm", Clips(SciFi + "/Loops/loop_energy.wav"), ui, 0.4f, blend: 0f, voices: 1),
                end: Cue("MatchEnd", Clips("Assets/Sound/UI/273736__squashy555__computer-startup.wav"), ui, 0.8f, blend: 0f, voices: 1),
                click: Cue("UIClick", ClipsIn("Assets/Sound/UI/KeyboardSounds"), ui, 0.7f, blend: 0f, voices: 2),
                navigate: Cue("UINavigate", ClipsIn("Assets/Sound/UI/ComputerWirrSounds"), ui, 0.4f, blend: 0f, voices: 2, cooldown: 0.05f));

            AssetDatabase.CreateAsset(library, LibraryPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Created {LibraryPath}. Placeholder picks: GunFire, Dash and HitConfirm, swap them on their cue assets.");
            return library;
        }

        /// <summary>Effect prefab sounds (rocket, explosions) follow the Effects volume.</summary>
        private static void RouteCombatEffects(AudioMixer mixer)
        {
            var settings = AssetDatabase.LoadAssetAtPath<CombatEffectsSettings>(EffectsSettingsPath);
            if (settings == null || settings.EffectsMixerGroup != null) return;
            settings.SetEffectsMixerGroup(Group(mixer, "Effects"));
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static SoundCue Cue(string name, AudioClip[] clips, AudioMixerGroup group, float volume, float blend, int voices,
            float cooldown = 0f, float pitch = 1f)
        {
            var cue = ScriptableObject.CreateInstance<SoundCue>();
            cue.Initialise(clips, group, volume, blend, voices, cooldown, pitch);
            AssetDatabase.CreateAsset(cue, $"{Folder}/Cue_{name}.asset");
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

        private static AudioMixerGroup Group(AudioMixer mixer, string name)
        {
            var group = mixer.FindMatchingGroups(name).FirstOrDefault(g => g.name == name);
            if (group == null) Debug.LogWarning($"Audio setup: mixer has no '{name}' group.");
            return group;
        }
    }
}
