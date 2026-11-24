using System;
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
        [SerializeField, Tooltip("Visual bullet speed, m/s, for instant guns only (bullet speed 0, hitscan). A gun with its own bullet speed flies its tracers at that speed.")]
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
        [SerializeField] private float flareLifetime = 3.5f;
        [SerializeField, Tooltip("Seconds between flares in one drop: they fire out one by one, in time with the pops of the flare sound.")]
        private float flareInterval = 0.11f;

        [Header("Repair drone")]
        [SerializeField, Tooltip("Drone model. Pooled. Empty = a simple built-in quad drone with a green light.")]
        private GameObject repairDrone;
        [SerializeField, Tooltip("Metres from the chopper's centre while the drone heals it.")]
        private float droneOrbitRadius = 4f;
        [SerializeField, Tooltip("Metres above the chopper's centre while it heals.")]
        private float droneOrbitHeight = 2.6f;
        [SerializeField, Tooltip("Orbit speed, degrees per second.")]
        private float droneOrbitSpeed = 110f;
        [SerializeField, ColorUsage(false, true), Tooltip("Repair beam colour (HDR).")]
        private Color droneBeamColour = new(0.3f, 3f, 1.1f, 1f);

        [Header("Audio")]
        [SerializeField, Tooltip("Mixer group for sounds inside the effect prefabs (rocket launch and flight, explosions).")]
        private AudioMixerGroup effectsMixerGroup;
        [SerializeField, Tooltip("BulletRed has an explosion sound per bullet. Off: the gun fire sound comes from the Sound Library instead.")]
        private bool gunBulletAudio;

        [Header("Capture the Nuke pick-up zone")]
        [SerializeField, Tooltip("Looping shell round a free nuke (Sci-Fi Arsenal Hex Shield), scaled so its radius is your own " +
                                 "hook range. Pooled. Empty: no zone.")]
        private GameObject nukeZone;
        [SerializeField, Min(0.01f), Tooltip("The zone prefab's radius in metres at scale 1 (Sci-Fi Arsenal shields: 4).")]
        private float nukeZoneRadius = 4f;
        [SerializeField, Range(0f, 3f), Tooltip("Zone colour scale while your chopper is in hook range. Below 1: from inside, " +
                                                 "a brighter shell filled the whole view pink (24 Nov); the HUD line says you can hook.")]
        private float nukeZoneInRangeScale = 0.25f;
        [SerializeField, Tooltip("Looping light beam on a free nuke, seen from far away (Sci-Fi Arsenal Loot Rays). Pooled. " +
                                 "Empty: no beacon.")]
        private GameObject nukeBeacon;
        [SerializeField, Tooltip("Beacon scale in the prefab's own axes. Loot Rays is built lying down: Z is the beam's height " +
                                 "(about 8 m at 1). 4 x 4 x 10 reads from 200 m away (24 Nov).")]
        private Vector3 nukeBeaconScale = new(4f, 4f, 10f);

        [Header("Capture the Nuke blast (NukeBlastSystem)")]
        [SerializeField, Tooltip("The blast prefab (Editor/Setup/NukeBlastAssets; menu Chopper > Tools > Rebuild Nuke Blast): " +
                                 "mushroom cap and stem, shock rings, dust puffs, burn mark, wind strokes, flash core, " +
                                 "condensation shell, shock bubble, embers, debris. Empty: only the layers below.")]
        private GameObject nukeBlast;
        [SerializeField, Min(5f), Tooltip("Mushroom cap radius (m); the whole blast scales with it.")]
        private float nukeBlastRadius = 55f;
        [SerializeField, Min(10f), Tooltip("Height the cap rises to (m, at the default radius).")]
        private float nukeRiseHeight = 230f;
        [SerializeField, Min(0.5f), Tooltip("Seconds for most of the rise.")]
        private float nukeRiseSeconds = 9f;
        [SerializeField, Min(1f), Tooltip("Seconds the mushroom stands before it dissolves.")]
        private float nukeBlastSeconds = 22f;
        [SerializeField, Min(0.1f), Tooltip("Seconds of the dissolve.")]
        private float nukeFadeSeconds = 5f;
        [SerializeField, Min(10f), Tooltip("Shock front speed (m/s): the shake comes when it reaches you, not at the flash.")]
        private float nukeShockSpeed = 340f;
        [SerializeField, Min(0f), Tooltip("EMP: HUDs glitch within this distance (m) at the flash. 0 = off.")]
        private float nukeEmpRadius = 900f;
        [SerializeField, Tooltip("White additive material for the screen flash (a quad in front of the camera). Empty: none.")]
        private Material nukeScreenFlash;
        [SerializeField, Tooltip("Extra pack layers with the blast (ground fires). Ground layers start on the ground under it.")]
        private BlastLayer[] nukeBlastLayers = Array.Empty<BlastLayer>();
        [SerializeField, Range(0f, 1f), Tooltip("Camera shake (trauma) at the blast; less with distance.")]
        private float nukeBlastShake = 1f;
        [SerializeField, Min(1f), Tooltip("No shake beyond this distance (m).")]
        private float nukeBlastShakeRange = 1200f;
        [SerializeField, ColorUsage(false, true), Tooltip("The flash: a point light at the blast that fades out.")]
        private Color nukeFlashColour = new(1f, 0.82f, 0.6f, 1f);
        [SerializeField, Min(0f)] private float nukeFlashIntensity = 60f;
        [SerializeField, Min(1f), Tooltip("Flash light range (m).")] private float nukeFlashRange = 900f;
        [SerializeField, Min(0.05f), Tooltip("Seconds the flash takes to fade.")] private float nukeFlashSeconds = 3f;

        [Header("Rotor downwash (dust blown up under low choppers)")]
        [SerializeField, Tooltip("Particle material for the dust (URP Particles Unlit, transparent, soft round texture). " +
                                 "Empty: no downwash.")]
        private Material downwashMaterial;
        [SerializeField, Tooltip("Height above the surface (m) where the downwash starts; full strength at the surface.")]
        private float downwashHeight = 25f;
        [SerializeField, Tooltip("Dust ring radius (m) at the top of the downwash height and at the surface.")]
        private Vector2 downwashRadius = new(9f, 4f);
        [SerializeField, Tooltip("Dust puffs per second at full strength.")]
        private float downwashRate = 220f;
        [SerializeField, Tooltip("Outward speed of the dust at full strength, m/s.")]
        private float downwashSpeed = 16f;
        [SerializeField, Tooltip("Dust puff size (m) when it starts; it grows as it moves out.")]
        private float downwashSize = 3.5f;
        [SerializeField, Tooltip("Dust colour (the biome's dust: sand, snow, ash...). Alpha = thickness.")]
        private Color downwashColour = new(0.70f, 0.61f, 0.47f, 0.7f);
        [SerializeField, Tooltip("Camera shake per second for the own chopper at full strength (0..1 trauma).")]
        private float downwashShake = 0.15f;

        [Header("Boost flames (jet flames from the exhaust while boosting; uses the Damage Spark Material)")]
        [SerializeField, Tooltip("The jet flame cone at the exhaust (local space, fixed to the nozzle). Off = the boost shows only the trail.")]
        private bool boostFlameParticles = true;
        [SerializeField, Tooltip("Flame length (m) at full strength: speed times life.")]
        private float boostFlameLength = 7f;
        [SerializeField, Tooltip("Flame width (m) at the nozzle.")]
        private float boostFlameSize = 0.7f;
        [SerializeField, Tooltip("Flame particles per second at full strength, per exhaust point.")]
        private float boostFlameRate = 140f;
        [SerializeField, Tooltip("Extra particles at the start of a boost (the kick), per exhaust point.")]
        private int boostFlameBurst = 40;
        [SerializeField, ColorUsage(false, true), Tooltip("Hot core colour (HDR, so bloom catches it).")]
        private Color boostFlameCore = new(6f, 4.2f, 2.2f, 1f);
        [SerializeField, ColorUsage(false, true), Tooltip("Colour at the flame tip (HDR).")]
        private Color boostFlameTip = new(0.1f, 1.2f, 3.5f, 1f);
        [SerializeField, Range(0f, 1f), Tooltip("Random flicker of the flame brightness and length.")]
        private float boostFlameFlicker = 0.35f;
        [SerializeField, Tooltip("Strength rise speed when a boost starts (1/s).")]
        private float boostFlameRise = 18f;
        [SerializeField, Tooltip("Strength fall speed when a boost ends (1/s). High = fades out fast.")]
        private float boostFlameFall = 7f;
        [SerializeField, Range(0f, 1f), Tooltip("Flame size and rate for the own chopper while the camera is inside the cockpit.")]
        private float boostFlameCockpitScale = 0.15f;

        [Header("Boost trail (glowing streak left behind while boosting)")]
        [SerializeField, Tooltip("Seconds the trail lasts before it has faded. 0 = no trail.")]
        private float boostTrailTime = 0.9f;
        [SerializeField, Tooltip("Trail width (m) at the exhaust.")]
        private float boostTrailWidth = 0.35f;
        [SerializeField, ColorUsage(true, true), Tooltip("Trail colour at the exhaust (HDR); it fades to clear along the trail.")]
        private Color boostTrailColour = new(0.5f, 1.6f, 4f, 1f);

        [Header("Damage (sparks at hits, smoke and fire as the hull runs low)")]
        [SerializeField, Tooltip("Additive particle material for sparks and flames (URP Particles Unlit, HDR base colour so " +
                                 "bloom catches it). Empty: no sparks or fire.")]
        private Material damageSparkMaterial;
        [SerializeField, Tooltip("Alpha-blend particle material for damage smoke (URP Particles Simple Lit). Empty: no smoke.")]
        private Material damageSmokeMaterial;
        [SerializeField, Tooltip("Additive material for damage flames, with a 4 x 4 fireball flipbook. Empty: the spark material.")]
        private Material damageFlameMaterial;
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
        private float fireBelow = 0.35f;
        [SerializeField, Tooltip("Smoke puffs per second from one damage point at the lowest health.")]
        private float smokeRate = 40f;
        [SerializeField, Tooltip("Smoke puff size (m) when it leaves the hull; it grows as it drifts.")]
        private float smokeSize = 2f;
        [SerializeField] private Color lightSmokeColour = new(0.62f, 0.62f, 0.6f, 0.35f);
        [SerializeField] private Color heavySmokeColour = new(0.05f, 0.05f, 0.05f, 0.8f);
        [SerializeField, Tooltip("Dust puffs thrown up by a crash or a hard landing (downwash material and map dust colour).")]
        private int impactDust = 24;
        [SerializeField, Tooltip("Field repair: welding sparks per second while a chopper repairs (everyone sees them).")]
        private float repairSparkRate = 26f;
        [SerializeField, Tooltip("Repair spark colour (multiplies the spark material; HDR for bloom).")]
        private Color repairSparkColour = new(0.55f, 0.95f, 1f, 1f);
        [SerializeField, Tooltip("Field repair: green motes per second that rise off the hull while a chopper repairs (0 = none).")]
        private float healMoteRate = 18f;
        [SerializeField, Tooltip("Heal mote colour (multiplies the spark material; HDR for bloom). Softer than corrosion green.")]
        private Color healMoteColour = new(0.35f, 1.6f, 0.75f, 1f);
        [SerializeField, Tooltip("How fast heal motes rise (m/s).")]
        private float healMoteRise = 2.2f;
        [SerializeField, Range(0f, 1f), Tooltip("Strength of the green sheen that pulses over the hull while it repairs (0 = off).")]
        private float healSheen = 1f;
        [SerializeField, Tooltip("Seconds for the heal sheen to fade in and out.")]
        private float healSheenFade = 0.35f;
        [SerializeField, Tooltip("Damage over time (pilots VIPER, PYRE): particles per second while a chopper corrodes or burns.")]
        private float statusRate = 20f;
        [SerializeField, Tooltip("Corrosion sparks (multiplies the spark material; HDR for bloom).")]
        private Color corrosionColour = new(0.5f, 1f, 0.15f, 1f);
        [SerializeField, Tooltip("Pilot abilities: particles per second while one is on (shield shell, nanites, overdrive).")]
        private float abilityRate = 34f;
        [SerializeField] private Color shieldColour = new(0.3f, 0.6f, 1f, 1f);
        [SerializeField] private Color naniteColour = new(0.3f, 1f, 0.65f, 1f);
        [SerializeField, Tooltip("Endless-boost abilities (Overclock, Strip Down, Battering Ram).")]
        private Color overdriveColour = new(1f, 0.3f, 0.6f, 1f);
        [SerializeField, Tooltip("Blackout pulse ring.")] private Color blackoutColour = new(0f, 0.94f, 1f, 1f);

        [Header("One-shot effects")]
        [SerializeField, Tooltip("Seconds before a muzzle flash or impact is destroyed.")]
        private float oneShotLifetime = 3f;

        [Header("Chopper death")]
        [SerializeField, Tooltip("A wreck's explosion. Empty = the missile impact, three layered blasts.")]
        private GameObject chopperExplosion;
        [SerializeField, Tooltip("The wreck's explosion built from layers (fireball, flames, embers, smoke column). Not empty: " +
                                 "used instead of Chopper Explosion; the missile impact still gives the first flash and light.")]
        private BlastLayer[] chopperExplosionLayers = Array.Empty<BlastLayer>();
        [SerializeField, Range(0f, 1f), Tooltip("Camera shake (trauma) at the blast; less with distance.")]
        private float chopperExplosionShake = 0.7f;
        [SerializeField, Min(1f), Tooltip("No shake beyond this distance (m).")]
        private float chopperExplosionShakeRange = 150f;

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
        /// <summary>A wreck's explosion (Docs/ChopperDeathDesign.md); empty = the missile impact, layered.</summary>
        /// <summary>Setup: the layered wreck explosion, when none is set.</summary>
        public void FillExplosionLayers(BlastLayer[] layers, bool replace = false)
        {
            if (chopperExplosionLayers == null || chopperExplosionLayers.Length == 0 || replace) chopperExplosionLayers = layers;
        }

        public IReadOnlyList<BlastLayer> NukeBlastLayers => nukeBlastLayers ?? Array.Empty<BlastLayer>();
        public GameObject NukeBlast => nukeBlast;
        public float NukeBlastRadius => nukeBlastRadius;
        public float NukeRiseHeight => nukeRiseHeight;
        public float NukeRiseSeconds => nukeRiseSeconds;
        public float NukeBlastSeconds => nukeBlastSeconds;
        public float NukeFadeSeconds => nukeFadeSeconds;
        public float NukeShockSpeed => nukeShockSpeed;
        public float NukeEmpRadius => nukeEmpRadius;
        public Material NukeScreenFlash => nukeScreenFlash;
        public float NukeBlastShake => nukeBlastShake;
        public float NukeBlastShakeRange => nukeBlastShakeRange;
        public Color NukeFlashColour => nukeFlashColour;
        public float NukeFlashIntensity => nukeFlashIntensity;
        public float NukeFlashRange => nukeFlashRange;
        public float NukeFlashSeconds => nukeFlashSeconds;

        /// <summary>Editor only: fills the nuke blast layers if empty (or always, with <paramref name="replace"/>).</summary>
        public void FillNukeBlast(BlastLayer[] layers, GameObject blast, Material screenFlash, bool replace = false)
        {
            if (nukeBlastLayers == null || nukeBlastLayers.Length == 0 || replace) nukeBlastLayers = layers;
            if (nukeBlast == null || replace) nukeBlast = blast;
            if (nukeScreenFlash == null || replace) nukeScreenFlash = screenFlash;
        }

        public IReadOnlyList<BlastLayer> ChopperExplosionLayers => chopperExplosionLayers ?? Array.Empty<BlastLayer>();
        public GameObject ChopperExplosion => chopperExplosion != null ? chopperExplosion : MissileImpact;
        public float ChopperExplosionShake => chopperExplosionShake;
        public float ChopperExplosionShakeRange => chopperExplosionShakeRange;
        public GameObject Flare => flare;
        public GameObject RepairDrone => repairDrone;
        public float DroneOrbitRadius => droneOrbitRadius;
        public float DroneOrbitHeight => droneOrbitHeight;
        public float DroneOrbitSpeed => droneOrbitSpeed;
        public Color DroneBeamColour => droneBeamColour;
        public int FlaresPerDrop => flaresPerDrop;
        public float FlareLifetime => flareLifetime;
        public float FlareInterval => flareInterval;
        public AudioMixerGroup EffectsMixerGroup => effectsMixerGroup;
        public bool GunBulletAudio => gunBulletAudio;
        public GameObject NukeZone => nukeZone;
        public float NukeZoneRadius => nukeZoneRadius;
        public float NukeZoneInRangeScale => nukeZoneInRangeScale;
        public GameObject NukeBeacon => nukeBeacon;
        public Vector3 NukeBeaconScale => nukeBeaconScale;
        public Material DownwashMaterial => downwashMaterial;
        public float DownwashHeight => downwashHeight;
        public Vector2 DownwashRadius => downwashRadius;
        public float DownwashRate => downwashRate;
        public float DownwashSpeed => downwashSpeed;
        public float DownwashSize => downwashSize;
        public Color DownwashColour => downwashColour;
        public float DownwashShake => downwashShake;
        public float BoostFlameLength => boostFlameLength;
        public float BoostFlameSize => boostFlameSize;
        public bool BoostFlameParticles => boostFlameParticles;
        public float BoostFlameRate => boostFlameRate;
        public int BoostFlameBurst => boostFlameBurst;
        public Color BoostFlameCore => boostFlameCore;
        public Color BoostFlameTip => boostFlameTip;
        public float BoostFlameFlicker => boostFlameFlicker;
        public float BoostFlameRise => boostFlameRise;
        public float BoostFlameFall => boostFlameFall;
        public float BoostFlameCockpitScale => boostFlameCockpitScale;
        public float BoostTrailTime => boostTrailTime;
        public float BoostTrailWidth => boostTrailWidth;
        public Color BoostTrailColour => boostTrailColour;
        public Material DamageSparkMaterial => damageSparkMaterial;
        public Material DamageSmokeMaterial => damageSmokeMaterial;
        public Material DamageFlameMaterial => damageFlameMaterial != null ? damageFlameMaterial : damageSparkMaterial;
        /// <summary>The flames have their own (flipbook) material.</summary>
        public bool FlameFlipbook => damageFlameMaterial != null;
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
        public float HealMoteRate => healMoteRate;
        public Color HealMoteColour => healMoteColour;
        public float HealMoteRise => healMoteRise;
        public float HealSheen => healSheen;
        public float HealSheenFade => healSheenFade;
        public float StatusRate => statusRate;
        public Color CorrosionColour => corrosionColour;
        public float AbilityRate => abilityRate;
        public Color ShieldColour => shieldColour;
        public Color NaniteColour => naniteColour;
        public Color OverdriveColour => overdriveColour;
        public Color BlackoutColour => blackoutColour;

        public void CollectProblems(List<string> problems)
        {
            if (gunBullet == null) problems.Add($"{name}: no gun bullet prefab.");
            if (gunImpact == null) problems.Add($"{name}: no gun impact prefab.");
            if (rocket == null) problems.Add($"{name}: no rocket prefab.");
            if (rocketImpact == null) problems.Add($"{name}: no rocket impact prefab.");
            if (missile == null) problems.Add($"{name}: no missile prefab (rockets' visual is used).");
            if (flare == null) problems.Add($"{name}: no flare prefab, so flares are invisible.");
            if (nukeZone == null) problems.Add($"{name}: no nuke zone prefab, so Capture the Nuke shows no pick-up range.");
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
        /// <summary>Editor only: fills empty Capture the Nuke zone and beacon slots.</summary>
        public void FillNukeZone(GameObject zone, GameObject beacon)
        {
            if (nukeZone == null) nukeZone = zone;
            if (nukeBeacon == null) nukeBeacon = beacon;
        }

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
        public void FillDamage(Material sparks, Material smoke, Material flames)
        {
            if (damageFlameMaterial == null) damageFlameMaterial = flames;
            if (damageSparkMaterial == null) damageSparkMaterial = sparks;
            if (damageSmokeMaterial == null) damageSmokeMaterial = smoke;
        }

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void SetEffectsMixerGroup(AudioMixerGroup group) => effectsMixerGroup = group;
    }
}
