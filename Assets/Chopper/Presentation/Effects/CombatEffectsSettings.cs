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

        public void CollectProblems(List<string> problems)
        {
            if (gunBullet == null) problems.Add($"{name}: no gun bullet prefab.");
            if (gunImpact == null) problems.Add($"{name}: no gun impact prefab.");
            if (rocket == null) problems.Add($"{name}: no rocket prefab.");
            if (rocketImpact == null) problems.Add($"{name}: no rocket impact prefab.");
            if (missile == null) problems.Add($"{name}: no missile prefab (rockets' visual is used).");
            if (flare == null) problems.Add($"{name}: no flare prefab, so flares are invisible.");
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

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void SetEffectsMixerGroup(AudioMixerGroup group) => effectsMixerGroup = group;
    }
}
