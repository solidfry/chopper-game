using UnityEngine;
using UnityEngine.Audio;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Which effect prefabs combat uses. Defaults (menu Chopper → Effects → Add Combat Effects To Open Scene) are the
    /// Sci-Fi Arsenal prefabs the old game used. Any slot can be empty: that effect is then not shown.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Effects/Combat Effects Settings", fileName = "CombatEffectsSettings")]
    public sealed class CombatEffectsSettings : ScriptableObject
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
        public AudioMixerGroup EffectsMixerGroup => effectsMixerGroup;
        public bool GunBulletAudio => gunBulletAudio;

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

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void SetEffectsMixerGroup(AudioMixerGroup group) => effectsMixerGroup = group;
    }
}
