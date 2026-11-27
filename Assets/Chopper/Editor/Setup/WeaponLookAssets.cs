using System.Collections.Generic;
using Chopper.Presentation.Audio;
using Chopper.Presentation.Effects;
using Chopper.Simulation.Loadout;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// The weapon look library (Docs/WeaponFeelPlan.md). Kind defaults start empty: every slot falls back to the shared
    /// prefabs and cues. Gun families (step 2) each get a tracer colour, flash, impact, fire cue and camera kick.
    /// Creates what is missing; never overwrites a look or a family the owner has edited.
    /// </summary>
    internal static class WeaponLookAssets
    {
        private const string Folder = "Assets/Chopper/Data/Effects/WeaponLooks";
        private const string LibraryPath = Folder + "/WeaponLookLibrary.asset";
        private const string Modules = "Assets/Chopper/Data/Loadout/";
        private const string SciFi = "Assets/ThirdParty/Archanor/Sci-Fi Arsenal/Sci-Fi Effects/Prefabs/Combat/";
        private const string Guns = "Assets/Chopper/Audio/SFX/Guns/";
        private const string SciFiGuns = "Assets/ThirdParty/Sci-Fi Gun Sounds/";

        private sealed class GunFamily
        {
            public string Name;
            public string[] Modules;
            public string Projectile, Flash, Impact;
            public System.Func<AudioMixerGroup, SoundCue> Cue;
            public float Kick;
        }

        /// <summary>Step 2 gun families (owner can merge or retune: plain more / less).</summary>
        private static IEnumerable<GunFamily> GunFamilies()
        {
            // Fast buzz, thin yellow tracers, small flash, light kick.
            yield return new GunFamily
            {
                Name = "Minigun", Modules = new[] { "Nose_M134Minigun", "Nose_MinigunMk2" },
                Projectile = SciFi + "Missiles/Bullet/v1/BulletYellow.prefab",
                Flash = SciFi + "MuzzleFlash/FireSmall/YellowFireSmallMuzzleFlash.prefab",
                Impact = SciFi + "Explosions/Bullet/BulletImpactYellow.prefab",
                Cue = group => AudioAssets.Cue("Gun_Minigun", AudioAssets.ClipsIn(Guns + "Minigun"), group, 0.7f, blend: 1f, voices: 6,
                    muffle: CueMuffle.Outside),
                Kick = 0.012f,
            };
            // Thump: red tracer, big fire flash, more kick.
            yield return new GunFamily
            {
                Name = "Heavy MG", Modules = new[] { "Nose_HeavyMG" },
                Projectile = SciFi + "Missiles/Bullet/v1/BulletRed.prefab",
                Flash = SciFi + "MuzzleFlash/Fire/RedFireMuzzleFlash.prefab",
                Impact = SciFi + "Explosions/Bullet/BulletImpactRed.prefab",
                Cue = group => AudioAssets.Cue("Gun_HeavyMG", AudioAssets.ClipsIn(Guns + "HeavyMG"), group, 0.8f, blend: 1f, voices: 6,
                    muffle: CueMuffle.Outside),
                Kick = 0.025f,
            };
            // Slow, heavy rounds: fat orange tracer, fire blast on impact, strong kick.
            yield return new GunFamily
            {
                Name = "Chain Gun", Modules = new[] { "Nose_ChainGun" },
                Projectile = SciFi + "Missiles/Bullet/v2/Bullet2Yellow.prefab",
                Flash = SciFi + "MuzzleFlash/Fire/OrangeFireMuzzleFlash.prefab",
                Impact = SciFi + "Explosions/Fire/V2/YellowFireImpactV2.prefab",
                Cue = group => AudioAssets.Cue("Gun_ChainGun", AudioAssets.ClipsIn(Guns + "ChainGun"), group, 0.9f, blend: 1f, voices: 4,
                    muffle: CueMuffle.Outside),
                Kick = 0.07f,
            };
            // Cannon shells: fireball flash, flak-like red puffs.
            yield return new GunFamily
            {
                Name = "Autocannon", Modules = new[] { "Nose_20mmAutocannon", "Nose_RotaryCannon" },
                Projectile = SciFi + "Missiles/Bullet/v2/Bullet2Red.prefab",
                Flash = SciFi + "MuzzleFlash/FireBall/FireBallMuzzleFlashRed.prefab",
                Impact = SciFi + "Explosions/Fire/V2/RedFireImpactV2.prefab",
                Cue = group => AudioAssets.Cue("Gun_Autocannon", AudioAssets.ClipsIn(Guns + "Autocannon"), group, 0.8f, blend: 1f, voices: 6,
                    muffle: CueMuffle.Outside),
                Kick = 0.04f,
            };
            // One heavy blue bolt, sharp crack, big kick.
            yield return new GunFamily
            {
                Name = "Railgun", Modules = new[] { "Nose_Railgun" },
                Projectile = SciFi + "Missiles/Laserbolt/BlueLaserBoltMissile.prefab",
                Flash = SciFi + "MuzzleFlash/Laser/LaserMuzzleFlashBlue.prefab",
                Impact = SciFi + "Explosions/LaserBolt/BlueLaserBoltImpact.prefab",
                Cue = group => AudioAssets.Cue("Gun_Railgun", AudioAssets.Clips(SciFiGuns + "Weapon 09 Shoot 1.wav", SciFiGuns + "Weapon 09 Shoot 2.wav"),
                    group, 0.9f, blend: 1f, voices: 2, muffle: CueMuffle.Outside),
                Kick = 0.3f,
            };
            // Green bolts, no shells, zap.
            yield return new GunFamily
            {
                Name = "Pulse Laser", Modules = new[] { "Nose_PulseLaser" },
                Projectile = SciFi + "Missiles/LaserSmall/GreenLaserSmallMissile.prefab",
                Flash = SciFi + "MuzzleFlash/Laser/LaserMuzzleFlashGreen.prefab",
                Impact = SciFi + "Explosions/LaserSmall/GreenLaserSmallImpact.prefab",
                Cue = group => AudioAssets.Cue("Gun_PulseLaser", AudioAssets.Clips(SciFiGuns + "Weapon 07 Shoot 1.wav", SciFiGuns + "Weapon 02 Shoot 1.wav"),
                    group, 0.6f, blend: 1f, voices: 6, muffle: CueMuffle.Outside),
                Kick = 0.008f,
            };
        }

        public static WeaponLookLibrary Ensure(AudioMixer mixer)
        {
            System.IO.Directory.CreateDirectory(Folder);
            var library = AssetDatabase.LoadAssetAtPath<WeaponLookLibrary>(LibraryPath);
            var created = library == null;
            if (created) library = ScriptableObject.CreateInstance<WeaponLookLibrary>();

            var gun = Look("Look_GunDefault", out _);
            var rocket = Look("Look_RocketDefault", out _);
            var guided = Look("Look_GuidedDefault", out _);
            if (created)
            {
                library.Initialise(gun, rocket, guided);
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            else if (library.FillDefaults(gun, rocket, guided)) EditorUtility.SetDirty(library);

            var effects = mixer != null ? AudioAssets.Group(mixer, "Effects") : null;
            if (AddMissingFamilies(library, GunFamilies(), effects)) EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        private static bool AddMissingFamilies(WeaponLookLibrary library, IEnumerable<GunFamily> wanted, AudioMixerGroup effects)
        {
            var families = new List<WeaponLookLibrary.Family>(library.Families);
            var added = false;
            foreach (var family in wanted)
            {
                if (families.Exists(f => f != null && f.Name == family.Name)) continue;
                var look = Look("Look_" + family.Name.Replace(" ", ""), out var fresh);
                if (fresh)
                {
                    look.Initialise(Prefab(family.Projectile), Prefab(family.Flash), Prefab(family.Impact),
                        effects != null ? family.Cue(effects) : null, family.Kick);
                    EditorUtility.SetDirty(look);
                }
                var members = new List<ModuleDefinition>();
                foreach (var module in family.Modules)
                {
                    var definition = AssetDatabase.LoadAssetAtPath<ModuleDefinition>($"{Modules}{module}.asset");
                    if (definition != null) members.Add(definition);
                    else Debug.LogWarning($"Weapon looks: no module at {Modules}{module}.asset.");
                }
                families.Add(new WeaponLookLibrary.Family(family.Name, look, members));
                added = true;
            }
            if (added) library.SetFamilies(families);
            return added;
        }

        private static GameObject Prefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Debug.LogWarning($"Weapon looks: no prefab at {path}.");
            return prefab;
        }

        private static WeaponLook Look(string assetName, out bool created)
        {
            var path = $"{Folder}/{assetName}.asset";
            var look = AssetDatabase.LoadAssetAtPath<WeaponLook>(path);
            created = look == null;
            if (!created) return look;
            look = ScriptableObject.CreateInstance<WeaponLook>();
            AssetDatabase.CreateAsset(look, path);
            return look;
        }
    }
}
