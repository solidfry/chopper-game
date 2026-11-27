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
            /// <summary>Guns: tracer size (owner 27 Nov: thinner than the pack's).</summary>
            public float Tracer = 1f;
            /// <summary>Guns: tracer brightness (owner 27 Nov: the pack's read too faint).</summary>
            public float Bright = 1.8f;
            /// <summary>Guns: the echo after a shot.</summary>
            public System.Func<AudioMixerGroup, SoundCue> Tail;
            /// <summary>Rockets: a sound that follows the round (bomb whistle).</summary>
            public System.Func<AudioMixerGroup, SoundCue> Flight;
            /// <summary>Rockets: a prefab built by setup (true-size model), instead of a path.</summary>
            public System.Func<GameObject> BuildProjectile;
            /// <summary>A blast prefab built by setup, instead of a path.</summary>
            public System.Func<GameObject> BuildImpact;
            public bool ScaleWithBlast = true;
            public bool Dropped;
            /// <summary>Cluster missiles: bomblet model, split flash and split sound.</summary>
            public string Bomblet, SplitFlash;
            public System.Func<AudioMixerGroup, SoundCue> SplitCue;
        }

        /// <summary>Step 4 guided families. Homing Missiles keep the guided default (red missile).</summary>
        private static IEnumerable<GunFamily> GuidedFamilies()
        {
            // Orange missile for the ground seeker, so it reads apart from the homing missile.
            yield return new GunFamily
            {
                Name = "Ground Seeker", Modules = new[] { "Pylon_GroundSeeker" },
                Projectile = SciFi + "Missiles/Rockets/OrangeRocketMissile.prefab",
                Flash = SciFi + "MuzzleFlash/Rocket/RocketMuzzleFlashOrange.prefab",
            };
            // Green carrier with a heavier launch; at the split a yellow fireball pops and yellow bomblets spread.
            yield return new GunFamily
            {
                Name = "Cluster Missiles", Modules = new[] { "Pylon_ClusterMissiles" },
                Projectile = SciFi + "Missiles/Rockets/GreenRocketMissile.prefab",
                Flash = SciFi + "MuzzleFlash/Rocket/RocketMuzzleFlashGreen.prefab",
                Cue = group => AudioAssets.Cue("Missile_ClusterLaunch", AudioAssets.Clips(SciFiGuns + "Weapon 15 Shoot 1.wav", SciFiGuns + "Weapon 15 Shoot 2.wav"),
                    group, 0.8f, blend: 1f, voices: 4, muffle: CueMuffle.Outside),
                Bomblet = SciFi + "Missiles/Spheres/v1/YellowSphereMissile.prefab",
                SplitFlash = SciFi + "MuzzleFlash/FireBall/FireBallMuzzleFlashYellow.prefab",
                SplitCue = group => AudioAssets.Cue("Missile_ClusterSplit", AudioAssets.Clips(SciFiGuns + "Additional Weapon Sounds 3_1.wav"),
                    group, 0.8f, blend: 1f, voices: 3, muffle: CueMuffle.Outside),
            };
        }

        private const string Tails = "Assets/Chopper/Audio/SFX/Guns/Tails/";

        /// <summary>Open-air echo after light shots: short gun tails, one at most every 0.5 s.</summary>
        internal static SoundCue TailLight(AudioMixerGroup group) => AudioAssets.Cue("Gun_TailLight",
            AudioAssets.Clips(Tails + "Gun Tail 05.wav", Tails + "Gun Tail 06.wav", Tails + "Gun Tail 07.wav", Tails + "Gun Tail 09.wav"),
            group, 0.45f, blend: 1f, voices: 2, cooldown: 0.5f, muffle: CueMuffle.Outside);

        /// <summary>Heavier echo for cannons: one at most every 0.6 s.</summary>
        internal static SoundCue TailHeavy(AudioMixerGroup group) => AudioAssets.Cue("Gun_TailHeavy",
            AudioAssets.Clips(Tails + "Tail Small 01.wav", Tails + "Tail Small 02.wav", Tails + "Gun Tail 03.wav"),
            group, 0.6f, blend: 1f, voices: 2, cooldown: 0.6f, muffle: CueMuffle.Outside);

        /// <summary>The railgun's long rumble: one at a time.</summary>
        internal static SoundCue TailRail(AudioMixerGroup group) => AudioAssets.Cue("Gun_TailRail",
            AudioAssets.Clips(Tails + "Tail Big 02.wav"), group, 0.6f, blend: 1f, voices: 1, cooldown: 1.5f, muffle: CueMuffle.Outside);

        private const string RocketPrefabs = "Assets/Chopper/Prefabs/Weapons";
        private const string Incoming = "Assets/Sound/Explosions/TomWinandySFX_Explosions Volume I_Incoming_";

        /// <summary>Step 3 pylon families. Pods (Rocket Pod, Mk II, Salvo, Swarm) keep the rocket default.</summary>
        private static IEnumerable<GunFamily> RocketFamilies()
        {
            // Thin yellow rockets, small yellow blast.
            yield return new GunFamily
            {
                Name = "Light Rockets", Modules = new[] { "Pylon_LightRockets" },
                Projectile = SciFi + "Missiles/Rockets/YellowRocketMissile.prefab",
                Flash = SciFi + "MuzzleFlash/Rocket/RocketMuzzleFlashYellow.prefab",
                Impact = SciFi + "Explosions/Rockets/Impact v1/YellowRocketImpact.prefab",
            };
            // The standard rocket with a bigger fire blast.
            yield return new GunFamily
            {
                Name = "Heavy Rockets", Modules = new[] { "Pylon_HeavyRockets" },
                Impact = SciFi + "Explosions/Rockets/Impact v2/RedFireRocketImpact.prefab",
            };
            // Purple rockets that burst in a sphere at the fuse.
            yield return new GunFamily
            {
                Name = "Flak Rockets", Modules = new[] { "Pylon_FlakRockets" },
                Projectile = SciFi + "Missiles/Rockets/PurpleRocketMissile.prefab",
                Flash = SciFi + "MuzzleFlash/Rocket/RocketMuzzleFlashPurple.prefab",
                Impact = SciFi + "Explosions/Spheres/v1/YellowSphereImpact.prefab",
            };
            // A real bomb model (PolygonMilitary jet bomb), dropped with no flash, whistles as it falls.
            yield return new GunFamily
            {
                Name = "Bombs", Modules = new[] { "Pylon_Bombs" },
                BuildProjectile = BuildBomb,
                Impact = SciFi + "Explosions/Rockets/Impact v2/RedFireRocketImpact.prefab",
                Flight = group => AudioAssets.Cue("Bomb_Whistle", AudioAssets.Clips(Incoming + "12.wav"), group, 0.7f, blend: 1f, voices: 4,
                    muffle: CueMuffle.Outside),
                ScaleWithBlast = false, Dropped = true,
            };
            // A dark spiked sphere with a blinking red light; a red sphere burst.
            yield return new GunFamily
            {
                Name = "Air Mines", Modules = new[] { "Pylon_AirMines" },
                BuildProjectile = BuildMine,
                BuildImpact = BuildMineBlast,
                ScaleWithBlast = false, Dropped = true,
            };
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
                Kick = 0.05f, Tracer = 0.4f, Tail = TailLight,
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
                Kick = 0.1f, Tracer = 0.55f, Tail = TailLight,
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
                Kick = 0.25f, Tracer = 0.6f, Tail = TailHeavy,
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
                Kick = 0.16f, Tracer = 0.6f, Tail = TailHeavy,
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
                Kick = 0.5f, Tracer = 0.6f, Tail = TailRail,
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
                Kick = 0.035f, Tracer = 0.5f, Tail = TailLight,
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
            if (AddMissingFamilies(library, RocketFamilies(), effects)) EditorUtility.SetDirty(library);
            if (AddMissingFamilies(library, GuidedFamilies(), effects)) EditorUtility.SetDirty(library);
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
                    var projectile = family.BuildProjectile != null ? family.BuildProjectile() : Prefab(family.Projectile);
                    look.Initialise(projectile, Prefab(family.Flash), family.BuildImpact != null ? family.BuildImpact() : Prefab(family.Impact),
                        effects != null && family.Cue != null ? family.Cue(effects) : null, family.Kick,
                        effects != null && family.Flight != null ? family.Flight(effects) : null, family.ScaleWithBlast, family.Dropped);
                    look.SetTracerScale(family.Tracer);
                    look.SetTracerBrightness(family.Bright);
                    if (effects != null && family.Tail != null) look.SetTail(family.Tail(effects));
                    if (family.Bomblet != null || family.SplitFlash != null)
                        look.SetCluster(Prefab(family.Bomblet), Prefab(family.SplitFlash),
                            effects != null && family.SplitCue != null ? family.SplitCue(effects) : null);
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
            if (string.IsNullOrEmpty(path)) return null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Debug.LogWarning($"Weapon looks: no prefab at {path}.");
            return prefab;
        }

        // ---- Built projectile models (step 3) ----

        private const string BombModel = "Assets/ThirdParty/PolygonMilitary/Prefabs/Vehicles/SM_Veh_Jet_Attach_Bomb_01.prefab";

        /// <summary>The jet bomb (3 m, nose +Z) at 0.6 scale: about 1.8 m, so it reads at range.</summary>
        private static GameObject BuildBomb()
        {
            var path = RocketPrefabs + "/Bomb.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            System.IO.Directory.CreateDirectory(RocketPrefabs);
            var root = new GameObject("Bomb");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BombModel);
            if (model != null)
            {
                var body = Object.Instantiate(model, root.transform);
                body.name = "Body";
                body.transform.localScale = Vector3.one * 0.6f;
                StripColliders(body);
            }
            else Debug.LogWarning($"Weapon looks: no bomb model at {BombModel}.");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>An air mine: dark sphere with six spikes, a red glow and a blinking red light (<see cref="BlinkLight"/>).</summary>
        private static GameObject BuildMine()
        {
            var path = RocketPrefabs + "/AirMine.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            System.IO.Directory.CreateDirectory(RocketPrefabs);
            var bodyMaterial = MakeMaterial(RocketPrefabs + "/Mat_AirMine_Body.mat", "Universal Render Pipeline/Lit", new Color(0.12f, 0.13f, 0.12f), 0.6f);
            var glowMaterial = MakeMaterial(RocketPrefabs + "/Mat_AirMine_Glow.mat", "Universal Render Pipeline/Unlit", new Color(1f, 0.1f, 0.05f), 0f);

            var root = new GameObject("AirMine");
            var shell = Primitive(PrimitiveType.Sphere, root.transform, "Shell", Vector3.zero, Vector3.one * 0.7f, Quaternion.identity, bodyMaterial);
            var axes = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            foreach (var axis in axes)
                Primitive(PrimitiveType.Cylinder, shell.transform, "Spike", axis * 0.55f, new Vector3(0.08f, 0.15f, 0.08f),
                    Quaternion.FromToRotation(Vector3.up, axis), bodyMaterial);
            var glow = Primitive(PrimitiveType.Sphere, root.transform, "Glow", new Vector3(0f, 0.3f, 0f), Vector3.one * 0.14f, Quaternion.identity, glowMaterial);
            var lightObject = new GameObject("BlinkLight");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.15f, 0.08f);
            light.range = 8f;
            light.shadows = LightShadows.None;
            root.AddComponent<BlinkLight>().Initialise(light, glow, 2.5f, 6f);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// The air mine's blast (owner 27 Nov: too small): the red sphere burst at 2.8x, every layer following the scale, so
        /// the sparks reach the 14 m damage radius, over a fire blast core at 1.5x.
        /// </summary>
        internal static GameObject BuildMineBlast()
        {
            var path = RocketPrefabs + "/MineBlast.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            System.IO.Directory.CreateDirectory(RocketPrefabs);
            var root = new GameObject("MineBlast");
            Layer(root.transform, SciFi + "Explosions/Spheres/v1/RedSphereImpact.prefab", 2.8f);
            Layer(root.transform, SciFi + "Explosions/Rockets/Impact v2/RedFireRocketImpact.prefab", 1.5f);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void Layer(Transform parent, string prefabPath, float scale)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (source == null)
            {
                Debug.LogWarning($"Weapon looks: no prefab at {prefabPath}.");
                return;
            }
            var layer = Object.Instantiate(source, parent);
            layer.name = source.name;
            layer.transform.localScale = Vector3.one * scale;
            foreach (var system in layer.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }

        private static GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 position, Vector3 scale,
            Quaternion rotation, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            StripColliders(part);
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }

        private static void StripColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
        }

        private static Material MakeMaterial(string path, string shaderName, Color colour, float smoothness)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(shaderName)) { color = colour };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.7f);
            AssetDatabase.CreateAsset(material, path);
            return material;
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
