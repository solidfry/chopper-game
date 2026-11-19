using Chopper.Presentation.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Combat effects settings, filled with the Sci-Fi Arsenal prefabs the old game used. Creates it if missing.
    /// </summary>
    internal static class EffectsAssets
    {
        private const string SettingsPath = "Assets/Chopper/Data/Effects/CombatEffectsSettings.asset";
        private const string SciFi = "Assets/ThirdParty/Archanor/Sci-Fi Arsenal/Sci-Fi Effects/Prefabs/Combat";

        private const string GunBullet = "Assets/Prefabs/Projectiles/ProjectilePrefabs/BulletRed.prefab";
        private const string GunMuzzleFlash = SciFi + "/MuzzleFlash/FireSmall/RedFireSmallMuzzleFlash.prefab";
        // The pack's original, not Prefabs/Particles/RedFireImpactClient (it holds a removed legacy script).
        private const string GunImpact = SciFi + "/Explosions/Fire/V1/RedFireImpact.prefab";
        private const string Rocket = "Assets/Prefabs/Projectiles/ProjectilePrefabs/Rocket/OrangeRocketMissile.prefab";
        private const string RocketMuzzleFlash = "Assets/Prefabs/Projectiles/ProjectilePrefabs/Rocket/RocketMuzzleFlashOrange.prefab";
        private const string RocketImpact = "Assets/Prefabs/Projectiles/ProjectilePrefabs/Rocket/RedFireRocketImpactV3.prefab";

        private const string Missile = SciFi + "/Missiles/Rockets/RedRocketMissile.prefab";
        private const string Flare = SciFi + "/Missiles/Sparkler/YellowSparklerMissile.prefab";

        private const string FireAndSmoke = "Assets/ThirdParty/NatureManufacture/Fire and Smoke Particles/Prefabs";

        /// <summary>
        /// The wreck's explosion (Docs/ChopperDeathDesign.md): a lit fireball, big flames, embers, then a smoke column
        /// that rises and drifts. Empty when the pack is not in the project (the blast is then the missile impact).
        /// </summary>
        private static BlastLayer[] ExplosionLayers()
        {
            // First the explosion itself: a big bright blast with two close pops and a shower of embers (the missile
            // impact gives the flash and light). Then the fire and smoke: a rolling fireball swells out of it, flames
            // burn on, late secondary pops (fuel, ammunition), and a smoke column swells and drifts.
            var layers = new[]
            {
                Layer(SmallBlast, 4.5f, 0f, 0f, 3f, 0f),
                Layer(SmallBlast, 3f, 0.06f, 0f, 3f, 0.5f, spread: 2.5f),
                Layer(SmallBlast, 3f, 0.12f, 0f, 3f, 1f, spread: 2.5f),
                Layer(Embers, 3f, 0f, 0.8f, 6f, 0.5f),
                Layer(Fireball, 6.5f, 0.25f, 1.2f, 10f, 0.5f, startScale: 2.5f, grow: 1f),
                Layer(Flames, 4.5f, 0.35f, 2.5f, 7f, 0f, startScale: 2f, grow: 1f, material: EnsureWreckFlameMaterial(), lifeColour: FlameLifeColour,
                    ground: true),
                Layer(SmallBlast, 2f, 0.6f, 0f, 3f, 1.5f, spread: 5f),
                Layer(RocketImpact, 1.6f, 1f, 0f, 3f, 0.5f, spread: 6f),
                // Dark fuel smoke, a little narrower (owner, 19 Nov: the pack's white column looked odd).
                Layer(Column, 3.2f, 0.7f, 6f, 22f, 2f, startScale: 1.2f, grow: 3f, ground: true, tint: ColumnTint),
            };
            return System.Array.TrueForAll(layers, l => l.Prefab != null) ? layers : System.Array.Empty<BlastLayer>();
        }

        private const string Fireball = FireAndSmoke + "/Smoke_Lit_Normal/Prefab_Explosion_Normal_Based_Lit.prefab";
        private const string Flames = FireAndSmoke + "/Other/Prefab_Fire_Huge_Flames_01.prefab";
        private const string Embers = FireAndSmoke + "/Other/Prefab_Fire_Embers_01.prefab";
        private const string Column = FireAndSmoke + "/Smoke_Lit_Normal/Prefab_Smoke_Normal_Based_Lit_3.prefab";
        /// <summary>The yellow rocket impact: the red one made the whole blast too red (owner).</summary>
        private const string SmallBlast = SciFi + "/Explosions/Rockets/Impact v3/YellowFireRocketImpactV3.prefab";

        private static BlastLayer Layer(string path, float scale, float delay, float emit, float life, float up,
            float startScale = 0f, float grow = 0f, float spread = 0f, Material material = null, Color lifeColour = default,
            bool ground = false, Color tint = default) => new()
        {
            Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path),
            Scale = scale, Delay = delay, EmitSeconds = emit, LifeSeconds = life, Up = up,
            StartScale = startScale, GrowSeconds = grow, Spread = spread, Material = material, LifeColour = lifeColour,
            Ground = ground, Tint = tint,
        };

        private static readonly Color ColumnTint = new(0.3f, 0.29f, 0.28f, 1f);

        private const string WreckFlameMaterialPath = "Assets/Chopper/Data/Effects/WreckFlames.mat";
        private const string PackFlameMaterial = "Assets/ThirdParty/NatureManufacture/Fire and Smoke Particles/Materials/Others/M_fire_flipbook_big_01.mat";
        /// <summary>
        /// HDR flame colour. The pack's grey (446) on its orange flipbook burns out to red-orange in the bloom (owner: "too
        /// red"); more green and blue against red keeps the hot core yellow-orange.
        /// </summary>
        /// <summary>The flames' peak colour over life: the pack's is deep red (0.28, 0.07, 0); this is a hotter orange-yellow.</summary>
        private static readonly Color FlameLifeColour = new(0.34f, 0.22f, 0.045f, 1f);
        /// <summary>
        /// 19 Nov: a third of the first value (260, 380, 700). Once the flame cards faced the camera, the old value blew
        /// out to a white core with a big red bloom (owner: "a glowing ball").
        /// </summary>
        private static readonly Color WreckFlameColour = new(90f, 130f, 240f, 0f);

        /// <summary>The pack's big flipbook flame, recoloured warmer-yellow (a copy: the pack is not changed).</summary>
        private static Material EnsureWreckFlameMaterial()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(PackFlameMaterial);
            if (source == null) return null;
            var material = AssetDatabase.LoadAssetAtPath<Material>(WreckFlameMaterialPath);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, WreckFlameMaterialPath);
            }
            material.SetColor("_Emission_Color", WreckFlameColour);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Tools: put the default explosion layers back (after tuning them in code).</summary>
        [MenuItem("Chopper/Tools/Reset Chopper Explosion Layers")]
        private static void ResetExplosionLayers()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CombatEffectsSettings>(SettingsPath);
            if (settings == null) return;
            settings.FillExplosionLayers(ExplosionLayers(), replace: true);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        public static CombatEffectsSettings Ensure(AudioMixer mixer)
        {
            var settings = AssetDatabase.LoadAssetAtPath<CombatEffectsSettings>(SettingsPath);
            if (settings != null)
            {
                // Repair: fill slots added after the asset was made (guided missiles, flares).
                settings.FillGuidedSlots(Load(Missile), Load(RocketImpact), Load(Flare));
                settings.FillDownwash(EnsureDownwashMaterial());
                settings.FillDamage(EnsureSparkMaterial(), EnsureSmokeMaterial(), EnsureFlameMaterial());
                settings.FillExplosionLayers(ExplosionLayers());
                DressExhaustSmoke();
                EditorUtility.SetDirty(settings);
                // Repair: route prefab sounds once the mixer exists.
                if (settings.EffectsMixerGroup == null && mixer != null)
                {
                    settings.SetEffectsMixerGroup(AudioAssets.Group(mixer, "Effects"));
                    EditorUtility.SetDirty(settings);
                }
                return settings;
            }

            settings = ScriptableObject.CreateInstance<CombatEffectsSettings>();
            settings.Initialise(Load(GunBullet), Load(GunMuzzleFlash), Load(GunImpact), Load(Rocket), Load(RocketMuzzleFlash), Load(RocketImpact));
            settings.FillGuidedSlots(Load(Missile), Load(RocketImpact), Load(Flare));
            settings.FillDownwash(EnsureDownwashMaterial());
            settings.FillDamage(EnsureSparkMaterial(), EnsureSmokeMaterial(), EnsureFlameMaterial());
            settings.FillExplosionLayers(ExplosionLayers());
            if (mixer != null) settings.SetEffectsMixerGroup(AudioAssets.Group(mixer, "Effects"));

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private const string DownwashMaterialPath = "Assets/Chopper/Data/Effects/DownwashDust.mat";
        private const string SparkMaterialPath = "Assets/Chopper/Data/Effects/DamageSparks.mat";
        private const string SmokeMaterialPath = "Assets/Chopper/Data/Effects/DamageSmoke.mat";
        private const string ExhaustMaterialPath = "Assets/Chopper/Data/Effects/ExhaustSmoke.mat";
        private const string ChopperGhostPath = "Assets/Chopper/Prefabs/ChopperGhost.prefab";

        /// <summary>
        /// Engine exhaust smoke on the base chopper prefab (the chassis prefabs inherit it): the two smoke systems (were
        /// the pack's Smoke_Small_Light_FX on a blank legacy material) get the smoke flipbook on a lit material that
        /// fades near the camera (the exhaust is right in front of the chase camera), turbulence, growth and lift, so it
        /// matches the downwash (owner, 18 Nov 2026). Runs again harmlessly: done once the material is set.
        /// </summary>
        internal static void DressExhaustSmoke()
        {
            var material = EnsureExhaustMaterial();
            if (material == null) return;
            var root = PrefabUtility.LoadPrefabContents(ChopperGhostPath);
            try
            {
                var changed = false;
                foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (system.transform.parent != root.transform) continue;
                    var renderer = system.GetComponent<ParticleSystemRenderer>();
                    if (renderer.sharedMaterial == material) continue;
                    DressExhaust(system, renderer, material);
                    changed = true;
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, ChopperGhostPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void DressExhaust(ParticleSystem system, ParticleSystemRenderer renderer, Material material)
        {
            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 220;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // Burnt fuel: a warm dark grey, a little see-through.
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.36f, 0.34f, 0.33f, 0.55f), new Color(0.5f, 0.48f, 0.46f, 0.45f));
            main.gravityModifier = -0.015f;

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 22f;
            // More smoke the faster it flies (a trail, not a cloud at the nozzle).
            emission.rateOverDistance = 0.6f;

            // A narrow cone out of the nozzle (the system faces back along the engine).
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.12f;

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.9f);
            noise.frequency = 0.45f;
            noise.scrollSpeed = 0.35f;
            noise.damping = true;
            noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(3f);
            limit.dampen = 0.08f;

            // Thin and tight at the nozzle, a soft spreading plume behind.
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1.6f), new Keyframe(1f, 4.5f)));

            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.85f, 0.88f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.08f), new GradientAlphaKey(0.45f, 0.5f), new GradientAlphaKey(0f, 1f) });
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(fade);

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            SmokeSheet.Apply(system);

            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = 1f;
        }

        /// <summary>Exhaust smoke: the damage smoke look (Simple Lit, alpha blend, soft, smoke flipbook) with a small
        /// glow and a camera fade (gone at 1.5 m, full at 7 m).</summary>
        private static Material EnsureExhaustMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ExhaustMaterialPath);
            if (material != null) return material;
            var smoke = EnsureSmokeMaterial();
            if (smoke == null) return null;
            material = new Material(smoke) { name = "ExhaustSmoke" };
            material.SetTexture("_BaseMap", EnsureSmokeSheet());
            material.SetColor("_EmissionColor", new Color(0.05f, 0.05f, 0.055f));
            material = Save(material, ExhaustMaterialPath, emission: true);
            CameraFade(material, 1.5f, 7f);
            return material;
        }

        private const string FlameMaterialPath = "Assets/Chopper/Data/Effects/DamageFlames.mat";
        private const string SciFiTextures = "Assets/ThirdParty/Archanor/Sci-Fi Arsenal/Sci-Fi Effects/Textures/";
        /// <summary>Spark streak (a soft teardrop, stretched along the spark's flight) from the Sci-Fi Arsenal pack.</summary>
        private const string SparkTexture = SciFiTextures + "bullet_modular.png";
        /// <summary>Fireball flipbook (4 x 4, played through) from the Sci-Fi Arsenal pack, white so the particle tints it.</summary>
        private const string FlameTexture = SciFiTextures + "fire4x4_modular.png";

        /// <summary>
        /// The pack texture, or Unity's round blob only if the pack is missing (owner rule, 18 Nov 2026: never use the
        /// default particle when the project has a proper one; a warning says when it falls back).
        /// </summary>
        private static Texture2D PackTexture(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null) return texture;
            Debug.LogWarning($"Combat effects: no texture at {path} (Sci-Fi Arsenal pack missing?); using Unity's default particle.");
            return AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        }

        /// <summary>Repair: an existing material that still has the round blob gets the pack texture.</summary>
        private static void UsePackTexture(Material material, string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null || material.GetTexture("_BaseMap") == texture) return;
            material.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(material);
        }

        /// <summary>Damage flames: the spark material's look (Unlit, additive, HDR) on the pack's fireball flipbook.</summary>
        private static Material EnsureFlameMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(FlameMaterialPath);
            if (material != null) return material;
            var sparks = EnsureSparkMaterial();
            if (sparks == null) return null;
            material = new Material(sparks) { name = "DamageFlames" };
            material.SetTexture("_BaseMap", PackTexture(FlameTexture));
            material.SetColor("_BaseColor", new Color(3f, 1.9f, 0.9f, 1f));
            material.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f, 0f, 0f));
            return Save(material, FlameMaterialPath, emission: false);
        }

        /// <summary>Damage sparks: URP Particles Unlit, additive, HDR base colour (about 4x) so bloom catches them, the
        /// pack's streak texture. Made once (an old one is repaired to the streak).</summary>
        private static Material EnsureSparkMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SparkMaterialPath);
            if (material != null)
            {
                UsePackTexture(material, SparkTexture);
                return material;
            }
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("Combat effects: URP Particles/Unlit shader not found; no damage spark material.");
                return null;
            }
            material = new Material(shader) { name = "DamageSparks" };
            material.SetTexture("_BaseMap", PackTexture(SparkTexture));
            material.SetColor("_BaseColor", new Color(4f, 2.6f, 1.2f, 1f));
            // Transparent, additive (Blend 2), no depth write, soft particles.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SoftParticlesEnabled", 1f);
            material.SetVector("_SoftParticleFadeParams", new Vector4(0f, 0.5f, 0f, 0f));
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return Save(material, SparkMaterialPath, emission: false);
        }

        /// <summary>Damage smoke: like the downwash dust (Simple Lit, alpha blend, soft) with almost no glow, so black
        /// smoke stays black. Made once.</summary>
        private static Material EnsureSmokeMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SmokeMaterialPath);
            if (material != null)
            {
                // Repair (18 Nov 2026): the smoke flipbook instead of the round blob.
                var sheet = EnsureSmokeSheet();
                if (sheet != null && material.GetTexture("_BaseMap") != sheet)
                {
                    material.SetTexture("_BaseMap", sheet);
                    EditorUtility.SetDirty(material);
                }
                Emissive(material);
                return material;
            }
            var shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            if (shader == null)
            {
                Debug.LogWarning("Combat effects: URP Particles/Simple Lit shader not found; no damage smoke material.");
                return null;
            }
            material = new Material(shader) { name = "DamageSmoke" };
            material.SetTexture("_BaseMap", EnsureSmokeSheet());
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SoftParticlesEnabled", 1f);
            material.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1.5f, 0f, 0f));
            material.SetColor("_EmissionColor", new Color(0.01f, 0.01f, 0.01f));
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return Save(material, SmokeMaterialPath, emission: true);
        }

        private const string SmokeSheetPath = "Assets/Chopper/Data/Effects/SmokeSheet.png";

        /// <summary>
        /// Shared smoke and dust flipbook, made in code (no texture pack needed): <see cref="SmokeSheet.Tiles"/> x
        /// <see cref="SmokeSheet.Tiles"/> frames. Each row is a different puff (a cluster of soft lobes broken up by
        /// noise); along a row the same puff spreads, thins and frays (left = young). Grey shading lit from the top left
        /// gives the lobes volume; the particle colour tints it. Particles pick a random row and play it over their
        /// life. Made once; delete the file to make it again.
        /// </summary>
        internal static Texture2D EnsureSmokeSheet()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeSheetPath);
            if (texture != null) return texture;

            const int frame = 256;
            var tiles = SmokeSheet.Tiles;
            var size = frame * tiles;
            var pixels = new Color32[size * size];
            var random = new System.Random(0x5309);
            for (var row = 0; row < tiles; row++)
            {
                // A puff: 5..7 lobes round the middle, and its own noise offset.
                var lobes = 5 + random.Next(3);
                var lobeCentres = new Vector2[lobes];
                var lobeRadii = new float[lobes];
                for (var l = 0; l < lobes; l++)
                {
                    var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    var reach = l == 0 ? 0f : 0.07f + (float)random.NextDouble() * 0.1f;
                    lobeCentres[l] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
                    lobeRadii[l] = (l == 0 ? 0.2f : 0.13f) + (float)random.NextDouble() * 0.06f;
                }
                var seed = new Vector2((float)random.NextDouble() * 100f, (float)random.NextDouble() * 100f);

                for (var column = 0; column < tiles; column++)
                {
                    var age = column / (float)(tiles - 1);
                    var spread = 1f + 0.22f * age;
                    var thin = 1f - 0.4f * age;
                    var fray = 0.05f + 0.3f * age;
                    // Unity textures start at the bottom row: row 0 of the sheet is the top row of the image.
                    var originX = column * frame;
                    var originY = (tiles - 1 - row) * frame;
                    for (var y = 0; y < frame; y++)
                    for (var x = 0; x < frame; x++)
                    {
                        var uv = new Vector2((x + 0.5f) / frame - 0.5f, (y + 0.5f) / frame - 0.5f);
                        var density = Density(uv, lobeCentres, lobeRadii, lobes, spread, thin, fray, seed, age);
                        // Light from the top left: thick smoke towards the light shades this point.
                        var towardLight = Density(uv + new Vector2(-0.035f, 0.035f), lobeCentres, lobeRadii, lobes, spread, thin, fray, seed, age);
                        var shade = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(towardLight * 1.1f - density * 0.35f));
                        var grey = (byte)Mathf.RoundToInt(Mathf.Clamp01(shade) * 255f);
                        pixels[(originY + y) * size + originX + x] = new Color32(grey, grey, grey, (byte)Mathf.RoundToInt(density * 255f));
                    }
                }
            }

            var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            image.SetPixels32(pixels);
            image.Apply();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SmokeSheetPath));
            System.IO.File.WriteAllBytes(SmokeSheetPath, image.EncodeToPNG());
            Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(SmokeSheetPath);
            if (AssetImporter.GetAtPath(SmokeSheetPath) is TextureImporter importer)
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeSheetPath);
        }

        /// <summary>Smoke density 0..1 at <paramref name="uv"/> (frame centre 0): soft lobes, eaten at the edge by noise.</summary>
        private static float Density(Vector2 uv, Vector2[] centres, float[] radii, int lobes, float spread, float thin, float fray,
            Vector2 seed, float age)
        {
            var body = 0f;
            for (var l = 0; l < lobes; l++)
            {
                var d = (uv - centres[l] * spread).magnitude / (radii[l] * spread);
                body += Mathf.Exp(-d * d * 2.2f);
            }
            body = 1f - Mathf.Exp(-body * 2.6f);
            // Billows: fractal noise, finer as the puff ages (it frays).
            var n = 0f;
            var amplitude = 0.5f;
            var frequency = 4f + 3f * age;
            for (var o = 0; o < 4; o++)
            {
                n += amplitude * Mathf.PerlinNoise(seed.x + uv.x * frequency, seed.y + uv.y * frequency);
                amplitude *= 0.5f;
                frequency *= 2.1f;
            }
            var density = Mathf.Clamp01((body * (0.55f + 0.9f * n) - fray) / (1f - fray)) * thin;
            // Never touch the frame edge (no cut lines between frames).
            var edge = Mathf.InverseLerp(0.49f, 0.4f, uv.magnitude);
            return density * Mathf.Clamp01(edge);
        }

        /// <summary>Saves a new particle material; sets the keywords again after CreateAsset (they can be lost
        /// before it, seen with the city plumes, 5 Nov 2026).</summary>
        private static Material Save(Material material, string path, bool emission)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_SOFTPARTICLES_ON");
            AssetDatabase.CreateAsset(material, path);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_SOFTPARTICLES_ON");
            if (emission) Emissive(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Rotor downwash dust: URP Particles Simple Lit (the chopper's spotlight and the city lights show it at
        /// night), a little glow so it never goes black, transparent alpha blend, Unity's soft round particle texture,
        /// colour from the particles (the map's dust colour). Made once.</summary>
        private static Material EnsureDownwashMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(DownwashMaterialPath);
            if (material != null)
            {
                // Repair (18 Nov 2026): the round blob looked fake; the dust now uses the smoke sheet (flipbook).
                var sheet = EnsureSmokeSheet();
                if (sheet != null && material.GetTexture("_BaseMap") != sheet)
                {
                    material.SetTexture("_BaseMap", sheet);
                    // The glow keyword was missing on the old asset; dust lit only by the night ambient was invisible.
                    material.SetColor("_EmissionColor", new Color(0.11f, 0.095f, 0.075f));
                    EditorUtility.SetDirty(material);
                }
                Emissive(material);
                CameraFade(material);
                return material;
            }
            var shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            if (shader == null)
            {
                Debug.LogWarning("Combat effects: URP Particles/Simple Lit shader not found; no downwash material.");
                return null;
            }
            material = new Material(shader) { name = "DownwashDust" };
            material.SetTexture("_BaseMap", EnsureSmokeSheet());
            material.SetColor("_BaseColor", Color.white);
            // Transparent, alpha blend, no depth write, soft particles (no hard line where dust meets the ground).
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SoftParticlesEnabled", 1f);
            material.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1.5f, 0f, 0f));
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_SOFTPARTICLES_ON");
            material.SetColor("_EmissionColor", new Color(0.11f, 0.095f, 0.075f));
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DownwashMaterialPath));
            AssetDatabase.CreateAsset(material, DownwashMaterialPath);
            // Keywords again on the asset: before CreateAsset they can be lost (seen with the city plumes, 5 Nov 2026).
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_SOFTPARTICLES_ON");
            Emissive(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Keeps the glow on: URP sets _EMISSION from the material's GI flags each time it validates the material, and
        /// "emissive is black" turned it off again (the dust was dark at night, 18 Nov 2026).
        /// </summary>
        /// <summary>
        /// Dust fades out close to the camera (gone at 3 m, full at 12 m): the pilot sees through the cloud round their
        /// own chopper, others still see it thick (owner: the big plumes blocked the view, 18 Nov 2026).
        /// </summary>
        private static void CameraFade(Material material, float near = 3f, float far = 12f)
        {
            var fade = new Vector4(near, far, 0f, 0f);
            if (material.IsKeywordEnabled("_FADING_ON") && material.GetFloat("_CameraFadingEnabled") > 0.5f) return;
            material.SetFloat("_CameraFadingEnabled", 1f);
            material.SetFloat("_CameraNearFadeDistance", fade.x);
            material.SetFloat("_CameraFarFadeDistance", fade.y);
            // URP packs them as (near, 1/(far-near)) (ParticleGUI).
            material.SetVector("_CameraFadeParams", new Vector4(fade.x, 1f / (fade.y - fade.x), 0f, 0f));
            material.EnableKeyword("_FADING_ON");
            EditorUtility.SetDirty(material);
        }

        internal static void Emissive(Material material)
        {
            if (material.globalIlluminationFlags == MaterialGlobalIlluminationFlags.RealtimeEmissive && material.IsKeywordEnabled("_EMISSION")) return;
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
        }

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Debug.LogWarning($"Combat effects: no prefab at {path}. Set it by hand on {SettingsPath}.");
            return prefab;
        }
    }
}
