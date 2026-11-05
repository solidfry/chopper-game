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
        private const string SciFi = "Assets/AssetBundles/Archanor/Sci-Fi Arsenal/Sci-Fi Effects/Prefabs/Combat";

        private const string GunBullet = "Assets/Prefabs/Projectiles/ProjectilePrefabs/BulletRed.prefab";
        private const string GunMuzzleFlash = SciFi + "/MuzzleFlash/FireSmall/RedFireSmallMuzzleFlash.prefab";
        // The pack's original, not Prefabs/Particles/RedFireImpactClient (it holds a removed legacy script).
        private const string GunImpact = SciFi + "/Explosions/Fire/V1/RedFireImpact.prefab";
        private const string Rocket = "Assets/Prefabs/Projectiles/ProjectilePrefabs/Rocket/OrangeRocketMissile.prefab";
        private const string RocketMuzzleFlash = "Assets/Prefabs/Projectiles/ProjectilePrefabs/Rocket/RocketMuzzleFlashOrange.prefab";
        private const string RocketImpact = "Assets/Prefabs/Projectiles/ProjectilePrefabs/Rocket/RedFireRocketImpactV3.prefab";

        private const string Missile = SciFi + "/Missiles/Rockets/RedRocketMissile.prefab";
        private const string Flare = SciFi + "/Missiles/Sparkler/YellowSparklerMissile.prefab";

        public static CombatEffectsSettings Ensure(AudioMixer mixer)
        {
            var settings = AssetDatabase.LoadAssetAtPath<CombatEffectsSettings>(SettingsPath);
            if (settings != null)
            {
                // Repair: fill slots added after the asset was made (guided missiles, flares).
                settings.FillGuidedSlots(Load(Missile), Load(RocketImpact), Load(Flare));
                settings.FillDownwash(EnsureDownwashMaterial());
                settings.FillDamage(EnsureSparkMaterial(), EnsureSmokeMaterial());
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
            settings.FillDamage(EnsureSparkMaterial(), EnsureSmokeMaterial());
            if (mixer != null) settings.SetEffectsMixerGroup(AudioAssets.Group(mixer, "Effects"));

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private const string DownwashMaterialPath = "Assets/Chopper/Data/Effects/DownwashDust.mat";
        private const string SparkMaterialPath = "Assets/Chopper/Data/Effects/DamageSparks.mat";
        private const string SmokeMaterialPath = "Assets/Chopper/Data/Effects/DamageSmoke.mat";

        /// <summary>Damage sparks and flames: URP Particles Unlit, additive, HDR base colour (about 4x) so bloom
        /// catches them. Made once.</summary>
        private static Material EnsureSparkMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SparkMaterialPath);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("Combat effects: URP Particles/Unlit shader not found; no damage spark material.");
                return null;
            }
            material = new Material(shader) { name = "DamageSparks" };
            material.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
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
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            if (shader == null)
            {
                Debug.LogWarning("Combat effects: URP Particles/Simple Lit shader not found; no damage smoke material.");
                return null;
            }
            material = new Material(shader) { name = "DamageSmoke" };
            material.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
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
            if (emission) material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Rotor downwash dust: URP Particles Simple Lit (the chopper's spotlight and the city lights show it at
        /// night), a little glow so it never goes black, transparent alpha blend, Unity's soft round particle texture,
        /// colour from the particles (the map's dust colour). Made once.</summary>
        private static Material EnsureDownwashMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(DownwashMaterialPath);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            if (shader == null)
            {
                Debug.LogWarning("Combat effects: URP Particles/Simple Lit shader not found; no downwash material.");
                return null;
            }
            material = new Material(shader) { name = "DownwashDust" };
            material.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
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
            material.SetColor("_EmissionColor", new Color(0.06f, 0.05f, 0.04f));
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DownwashMaterialPath));
            AssetDatabase.CreateAsset(material, DownwashMaterialPath);
            // Keywords again on the asset: before CreateAsset they can be lost (seen with the city plumes, 5 Nov 2026).
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_SOFTPARTICLES_ON");
            material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            return material;
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
