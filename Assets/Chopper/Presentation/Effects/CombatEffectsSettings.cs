using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;
using UnityEngine.Audio;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Which effect prefabs combat uses. Defaults (made by Chopper → Setup) are the
    /// Sci-Fi Arsenal prefabs the old game used. Any slot can be empty: that effect is then not shown.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Effects/Combat Effects Settings", fileName = "CombatEffectsSettings")]
    public sealed class CombatEffectsSettings : ScriptableObject, IValidatedAsset
    {
        [Header("Nose gun")]
        [SerializeField, Tooltip("Looping projectile visual, flown from the muzzle to where the shot ends. Pooled.")]
        private GameObject gunBullet;
        [SerializeField] private GameObject gunMuzzleFlash;
        [SerializeField] private GameObject gunImpact;
        [SerializeField, Tooltip("Visual bullet speed, m/s. Damage is instant (hitscan); this is only how it looks.")]
        private float gunBulletSpeed = 900f;
        [SerializeField, Tooltip("Least time between two muzzle flashes of one chopper, so fast guns do not spawn one per shot.")]
        private float gunMuzzleFlashInterval = 0.1f;

        [Header("Rockets")]
        [SerializeField, Tooltip("Looping projectile visual (body, smoke trail, light, sound). Pooled.")]
        private GameObject rocket;
        [SerializeField] private GameObject rocketMuzzleFlash;
        [SerializeField] private GameObject rocketImpact;
        [SerializeField, Tooltip("Seconds a rocket's smoke trail stays after it hits, before the pool takes it back.")]
        private float rocketTrailLinger = 2f;

        [Header("Guided missiles and flares")]
        [SerializeField, Tooltip("Looping visual that follows each missile ghost. Pooled.")]
        private GameObject missile;
        [SerializeField] private GameObject missileImpact;
        [SerializeField, Tooltip("Looping visual for one flare (falls and burns out). Pooled.")]
        private GameObject flare;
        [SerializeField] private int flaresPerDrop = 6;
        [SerializeField] private float flareLifetime = 2.5f;

        [Header("Audio")]
        [SerializeField, Tooltip("Mixer group for sounds inside the effect prefabs (rocket launch and flight, explosions).")]
        private AudioMixerGroup effectsMixerGroup;
        [SerializeField, Tooltip("BulletRed has an explosion sound per bullet. Off: the gun fire sound comes from the Sound Library instead.")]
        private bool gunBulletAudio;

        [Header("Rotor downwash (dust blown up under low choppers)")]
        [SerializeField, Tooltip("Particle material for the dust (URP Particles Unlit, transparent, soft round texture). " +
                                 "Empty: no downwash.")]
        private Material downwashMaterial;
        [SerializeField, Tooltip("Height above the surface (m) where the downwash starts; full strength at the surface.")]
        private float downwashHeight = 25f;
        [SerializeField, Tooltip("Dust ring radius (m) at the top of the downwash height and at the surface.")]
        private Vector2 downwashRadius = new(9f, 4f);
        [SerializeField, Tooltip("Dust puffs per second at full strength.")]
        private float downwashRate = 80f;
        [SerializeField, Tooltip("Outward speed of the dust at full strength, m/s.")]
        private float downwashSpeed = 16f;
        [SerializeField, Tooltip("Dust puff size (m) when it starts; it grows as it moves out.")]
        private float downwashSize = 3.5f;
        [SerializeField, Tooltip("Dust colour (the biome's dust: sand, snow, ash...). Alpha = thickness.")]
        private Color downwashColour = new(0.70f, 0.61f, 0.47f, 0.45f);
        [SerializeField, Tooltip("Camera shake per second for the own chopper at full strength (0..1 trauma).")]
        private float downwashShake = 0.15f;

        [Header("Damage (sparks at hits, smoke and fire as the hull runs low)")]
        [SerializeField, Tooltip("Additive particle material for sparks and flames (URP Particles Unlit, HDR base colour so " +
                                 "bloom catches it). Empty: no sparks or fire.")]
        private Material damageSparkMaterial;
        [SerializeField, Tooltip("Alpha-blend particle material for damage smoke (URP Particles Simple Lit). Empty: no smoke.")]
        private Material damageSmokeMaterial;
        [SerializeField, Tooltip("Sparks per point of damage in one hit; the count is kept inside Spark Count.")]
        private float sparksPerDamage = 1.5f;
        [SerializeField, Tooltip("Least and most sparks for one hit.")]
        private Vector2Int sparkCount = new(6, 48);
        [SerializeField, Tooltip("Spark speed off the hull, m/s.")]
        private float sparkSpeed = 16f;
        [SerializeField, Range(0f, 1f), Tooltip("Health share below which light smoke comes out where the chopper was hit.")]
        private float smokeBelow = 0.7f;
        [SerializeField, Range(0f, 1f), Tooltip("Health share below which the smoke is thick and black.")]
        private float heavySmokeBelow = 0.4f;
        [SerializeField, Range(0f, 1f), Tooltip("Health share below which the hit points also burn.")]
        private float fireBelow = 0.2f;
        [SerializeField, Tooltip("Smoke puffs per second from one damage point at the lowest health.")]
        private float smokeRate = 28f;
        [SerializeField, Tooltip("Smoke puff size (m) when it leaves the hull; it grows as it drifts.")]
        private float smokeSize = 1.4f;
        [SerializeField] private Color lightSmokeColour = new(0.62f, 0.62f, 0.6f, 0.35f);
        [SerializeField] private Color heavySmokeColour = new(0.05f, 0.05f, 0.05f, 0.8f);
        [SerializeField, Tooltip("Dust puffs thrown up by a crash or a hard landing (downwash material and map dust colour).")]
        private int impactDust = 24;
        [SerializeField, Tooltip("Field repair: welding sparks per second while a chopper repairs (everyone sees them).")]
        private float repairSparkRate = 26f;
        [SerializeField, Tooltip("Repair spark colour (multiplies the spark material; HDR for bloom).")]
        private Color repairSparkColour = new(0.55f, 0.95f, 1f, 1f);
        [SerializeField, Tooltip("Damage over time (pilots VIPER, PYRE): particles per second while a chopper corrodes or burns.")]
        private float statusRate = 20f;
        [SerializeField, Tooltip("Corrosion sparks (multiplies the spark material; HDR for bloom).")]
        private Color corrosionColour = new(0.5f, 1f, 0.15f, 1f);

        [Header("One-shot effects")]
        [SerializeField, Tooltip("Seconds before a muzzle flash or impact is destroyed.")]
        private float oneShotLifetime = 3f;

        public GameObject GunBullet => gunBullet;
        public GameObject GunMuzzleFlash => gunMuzzleFlash;
        public GameObject GunImpact => gunImpact;
        public float GunBulletSpeed => gunBulletSpeed;
        public float GunMuzzleFlashInterval => gunMuzzleFlashInterval;
        public GameObject Rocket => rocket;
        public GameObject RocketMuzzleFlash => rocketMuzzleFlash;
        public GameObject RocketImpact => rocketImpact;
        public float RocketTrailLinger => rocketTrailLinger;
        public float OneShotLifetime => oneShotLifetime;
        public GameObject Missile => missile != null ? missile : rocket;
        public GameObject MissileImpact => missileImpact != null ? missileImpact : rocketImpact;
        public GameObject Flare => flare;
        public int FlaresPerDrop => flaresPerDrop;
        public float FlareLifetime => flareLifetime;
        public AudioMixerGroup EffectsMixerGroup => effectsMixerGroup;
        public bool GunBulletAudio => gunBulletAudio;
        public Material DownwashMaterial => downwashMaterial;
        public float DownwashHeight => downwashHeight;
        public Vector2 DownwashRadius => downwashRadius;
        public float DownwashRate => downwashRate;
        public float DownwashSpeed => downwashSpeed;
        public float DownwashSize => downwashSize;
        public Color DownwashColour => downwashColour;
        public float DownwashShake => downwashShake;
        public Material DamageSparkMaterial => damageSparkMaterial;
        public Material DamageSmokeMaterial => damageSmokeMaterial;
        public float SparksPerDamage => sparksPerDamage;
        public Vector2Int SparkCount => sparkCount;
        public float SparkSpeed => sparkSpeed;
        public float SmokeBelow => smokeBelow;
        public float HeavySmokeBelow => heavySmokeBelow;
        public float FireBelow => fireBelow;
        public float SmokeRate => smokeRate;
        public float SmokeSize => smokeSize;
        public Color LightSmokeColour => lightSmokeColour;
        public Color HeavySmokeColour => heavySmokeColour;
        public int ImpactDust => impactDust;
        public float RepairSparkRate => repairSparkRate;
        public Color RepairSparkColour => repairSparkColour;
        public float StatusRate => statusRate;
        public Color CorrosionColour => corrosionColour;

        public void CollectProblems(List<string> problems)
        {
            if (gunBullet == null) problems.Add($"{name}: no gun bullet prefab.");
            if (gunImpact == null) problems.Add($"{name}: no gun impact prefab.");
            if (rocket == null) problems.Add($"{name}: no rocket prefab.");
            if (rocketImpact == null) problems.Add($"{name}: no rocket impact prefab.");
            if (missile == null) problems.Add($"{name}: no missile prefab (rockets' visual is used).");
            if (flare == null) problems.Add($"{name}: no flare prefab, so flares are invisible.");
            if (downwashMaterial == null) problems.Add($"{name}: no downwash material, so low choppers blow up no dust.");
            if (damageSparkMaterial == null) problems.Add($"{name}: no damage spark material, so hits show no sparks and damaged choppers no fire.");
            if (damageSmokeMaterial == null) problems.Add($"{name}: no damage smoke material, so damaged choppers do not smoke.");
            if (effectsMixerGroup == null) problems.Add($"{name}: no effects mixer group, so prefab sounds skip the volume setting.");
        }

        /// <summary>Editor only: used by the scene setup tool.</summary>
        public void Initialise(GameObject bullet, GameObject gunFlash, GameObject gunHit, GameObject rocketVisual,
            GameObject rocketFlash, GameObject rocketHit)
        {
            gunBullet = bullet;
            gunMuzzleFlash = gunFlash;
            gunImpact = gunHit;
            rocket = rocketVisual;
            rocketMuzzleFlash = rocketFlash;
            rocketImpact = rocketHit;
        }

        /// <summary>Editor only: fills empty guided-missile and flare slots.</summary>
        public void FillGuidedSlots(GameObject missileVisual, GameObject missileHit, GameObject flareVisual)
        {
            if (missile == null) missile = missileVisual;
            if (missileImpact == null) missileImpact = missileHit;
            if (flare == null) flare = flareVisual;
        }

        /// <summary>Editor only: fills an empty downwash material slot.</summary>
        public void FillDownwash(Material material)
        {
            if (downwashMaterial == null) downwashMaterial = material;
        }

        /// <summary>Editor only: fills empty damage material slots.</summary>
        public void FillDamage(Material sparks, Material smoke)
        {
            if (damageSparkMaterial == null) damageSparkMaterial = sparks;
            if (damageSmokeMaterial == null) damageSmokeMaterial = smoke;
        }

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void SetEffectsMixerGroup(AudioMixerGroup group) => effectsMixerGroup = group;
    }
}
