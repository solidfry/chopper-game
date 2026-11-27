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
            /// <summary>Guns: seconds the trail stays after the hit.</summary>
            public float Linger;
            /// <summary>Rockets: realistic blast layers with the impact.</summary>
            public System.Func<BlastLayer[]> Layers;
            /// <summary>Guns: fire loop, spin-up and spin-down cues (the minigun).</summary>
            public System.Func<AudioMixerGroup, (SoundCue Loop, SoundCue Up, SoundCue Down)> Spin;
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
        private const string Spin = "Assets/Chopper/Audio/SFX/Guns/MinigunSpin/";

        /// <summary>
        /// The minigun (owner 27 Nov: sound like a real one): a fast continuous gun loop in place of single shots, a motor
        /// whine up (0.5 s) and down. The loop is the gun pack's fastest burst (about 2,500 rounds a minute).
        /// </summary>
        internal static (SoundCue, SoundCue, SoundCue) MinigunSpin(AudioMixerGroup group) => (
            AudioAssets.Cue("Gun_MinigunLoop", AudioAssets.Clips(Spin + "FireLoop.wav"), group, 0.75f, blend: 1f, voices: 4,
                muffle: CueMuffle.Outside),
            AudioAssets.Cue("Gun_MinigunSpinUp", AudioAssets.Clips(Spin + "SpinUp.wav"), group, 0.45f, blend: 1f, voices: 2,
                muffle: CueMuffle.Outside),
            AudioAssets.Cue("Gun_MinigunSpinDown", AudioAssets.Clips(Spin + "SpinDown.wav"), group, 0.4f, blend: 1f, voices: 2,
                muffle: CueMuffle.Outside));

        /// <summary>The minigun's burst end: the pack's loop tails (the burst rings out).</summary>
        internal static SoundCue MinigunTail(AudioMixerGroup group) => AudioAssets.Cue("Gun_MinigunTail",
            AudioAssets.Clips(Spin + "LoopTail1.wav", Spin + "LoopTail2.wav"), group, 0.6f, blend: 1f, voices: 2, muffle: CueMuffle.Outside);

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
                BuildImpact = BuildHeavyBlast, ScaleWithBlast = false, Layers = HeavyLayers,
            };
            // Purple rockets that burst in a sphere at the fuse.
            yield return new GunFamily
            {
                Name = "Flak Rockets", Modules = new[] { "Pylon_FlakRockets" },
                Projectile = SciFi + "Missiles/Rockets/PurpleRocketMissile.prefab",
                Flash = SciFi + "MuzzleFlash/Rocket/RocketMuzzleFlashPurple.prefab",
                BuildImpact = BuildFlakBlast, ScaleWithBlast = false, Layers = FlakLayers,
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
                Kick = 0.05f, Tracer = 0.4f, Tail = MinigunTail, Spin = MinigunSpin,
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
                BuildProjectile = BuildRailBolt, Linger = 0.5f,
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
                    look.SetTrailLinger(family.Linger);
                    if (effects != null && family.Spin != null)
                    {
                        var (loop, up, down) = family.Spin(effects);
                        look.SetSpin(loop, up, down);
                    }
                    look.SetImpactLayers(family.Layers != null ? family.Layers() : null);
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
        internal static GameObject BuildMineBlast() => Blast("MineBlast",
            (SciFi + "Explosions/Spheres/v1/RedSphereImpact.prefab", 2.8f),
            (SciFi + "Explosions/Rockets/Impact v2/RedFireRocketImpact.prefab", 1.5f));

        /// <summary>
        /// Heavy rocket impact prefab: flying sparks and a shock ring sized for its 14 m radius. The fire and smoke come
        /// from <see cref="HeavyLayers"/>.
        /// </summary>
        internal static GameObject BuildHeavyBlast() => Blast("HeavyRocketBlast",
            (SciFi + "Explosions/Spheres/v1/YellowSphereImpact.prefab", 2.2f),
            (SciFi + "Explosions/Shockwave/YellowShockwaveImpact.prefab", 3f));

        /// <summary>
        /// The heavy rocket's body of the blast, from the wreck explosion's parts (owner 27 Nov: realistic, dangerous): two
        /// bright pops, embers, a swelling fireball and a short dark smoke column.
        /// </summary>
        internal static BlastLayer[] HeavyLayers() => Present(new[]
        {
            EffectsAssets.Layer(EffectsAssets.SmallBlast, 2.6f, 0f, 0f, 3f, 0f),
            EffectsAssets.Layer(EffectsAssets.SmallBlast, 1.8f, 0f, 0f, 3f, 0.8f, spread: 2f),
            EffectsAssets.Layer(EffectsAssets.Embers, 2f, 0f, 0.5f, 5f, 0.5f),
            EffectsAssets.Layer(EffectsAssets.Fireball, 3.5f, 0f, 0.6f, 7f, 0.5f, startScale: 1.5f, grow: 0.6f),
            EffectsAssets.Layer(EffectsAssets.Column, 1.6f, 0f, 2f, 12f, 1.5f, startScale: 0.8f, grow: 2f, tint: EffectsAssets.ColumnTint),
        });

        /// <summary>Flak: a bright pop and the black puff of a real flak burst that hangs in the air.</summary>
        internal static BlastLayer[] FlakLayers() => Present(new[]
        {
            EffectsAssets.Layer(EffectsAssets.SmallBlast, 1.6f, 0f, 0f, 3f, 0f),
            EffectsAssets.Layer(EffectsAssets.Fireball, 2.4f, 0f, 0.3f, 5f, 0f, startScale: 1f, grow: 0.4f, tint: new Color(0.12f, 0.11f, 0.1f, 1f)),
        });

        /// <summary>All layers, or none when a pack is missing (NatureManufacture is git-ignored).</summary>
        private static BlastLayer[] Present(BlastLayer[] layers) =>
            System.Array.TrueForAll(layers, l => l.Prefab != null) ? layers : System.Array.Empty<BlastLayer>();

        /// <summary>Flak airburst: a sharp yellow flash, a sphere of shrapnel and a shock ring, sized for its 9 m radius.</summary>
        internal static GameObject BuildFlakBlast() => Blast("FlakBlast",
            (SciFi + "Explosions/Spheres/v1/YellowSphereImpact.prefab", 2.2f),
            (SciFi + "Explosions/Sharp/YellowSharpImpact.prefab", 2f),
            (SciFi + "Explosions/Shockwave/YellowShockwaveImpact.prefab", 2f));

        private const string FireAndSmoke = "Assets/ThirdParty/NatureManufacture/Fire and Smoke Particles/Prefabs";

        /// <summary>
        /// A blast prefab from layers (path, scale); every layer's particles follow its scale. A negative scale marks a
        /// looping pack effect (the fireball emits 1 a second): it becomes a one-time burst of 4 at that size. Rebuilt each run.
        /// </summary>
        private static GameObject Blast(string name, params (string Path, float Scale)[] layers)
        {
            System.IO.Directory.CreateDirectory(RocketPrefabs);
            var root = new GameObject(name);
            foreach (var (path, scale) in layers)
            {
                var layer = Layer(root.transform, path, Mathf.Abs(scale));
                if (scale >= 0f || layer == null) continue;
                foreach (var system in layer.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = system.main;
                    main.loop = false;
                    main.duration = 1f;
                    var emission = system.emission;
                    emission.rateOverTime = 0f;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 4) });
                }
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RocketPrefabs + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Railgun round (owner 27 Nov): the blue bolt with a long white-to-blue trail that hangs for about half a second
        /// (the look's trail linger keeps it after the hit).
        /// </summary>
        internal static GameObject BuildRailBolt()
        {
            System.IO.Directory.CreateDirectory(RocketPrefabs);
            var root = new GameObject("RailgunBolt");
            Layer(root.transform, SciFi + "Missiles/Laserbolt/BlueLaserBoltMissile.prefab", 1f);
            var tail = new GameObject("Tail").AddComponent<TrailRenderer>();
            tail.transform.SetParent(root.transform, false);
            tail.time = 0.5f;
            tail.minVertexDistance = 0.5f;
            tail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 0f));
            tail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tail.receiveShadows = false;
            tail.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/ThirdParty/Archanor/Sci-Fi Arsenal/Sci-Fi Effects/Materials/Trails/trailblue.mat");
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.3f, 0.6f, 1f), 0.25f), new GradientColorKey(new Color(0.1f, 0.3f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            tail.colorGradient = gradient;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RocketPrefabs + "/RailgunBolt.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject Layer(Transform parent, string prefabPath, float scale)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (source == null)
            {
                Debug.LogWarning($"Weapon looks: no prefab at {prefabPath}.");
                return null;
            }
            var layer = Object.Instantiate(source, parent);
            layer.name = source.name;
            layer.transform.localScale = Vector3.one * scale;
            foreach (var system in layer.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            return layer;
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
