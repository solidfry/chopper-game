using System.Collections.Generic;
using Chopper.Presentation.Audio;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// How one weapon family looks and sounds (Docs/WeaponFeelPlan.md): one asset per family, not per module (a
    /// module and its Mk tiers share one). Every slot is optional: an empty slot uses the shared one in
    /// <see cref="CombatEffectsSettings"/> / <see cref="SoundLibrary"/>, so an empty look changes nothing.
    /// Presentation only: the simulation knows only the module id (<see cref="WeaponLookLibrary"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Effects/Weapon Look", fileName = "WeaponLook")]
    public sealed class WeaponLook : ScriptableObject, IValidatedAsset
    {
        [SerializeField, Tooltip("Tracer (guns) or round in flight (rockets, bombs, mines). Empty = the shared one.")]
        private GameObject projectile;
        [SerializeField, Tooltip("Flash at the muzzle or launch rail. Empty = the shared one.")]
        private GameObject muzzleFlash;
        [SerializeField, Tooltip("Hit / blast. Empty = the shared one.")]
        private GameObject impact;
        [SerializeField, Tooltip("Fire sound (guns: per shot; rockets: on top of the launch flash). Empty = the shared " +
                                 "gun cue for guns, none for rockets.")]
        private SoundCue fireCue;

        public GameObject Projectile => projectile;
        public GameObject MuzzleFlash => muzzleFlash;
        public GameObject Impact => impact;
        public SoundCue FireCue => fireCue;

        public void CollectProblems(List<string> problems)
        {
            // Every slot may be empty (falls back); nothing to check yet.
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(GameObject projectilePrefab, GameObject flash, GameObject hit, SoundCue fire)
        {
            projectile = projectilePrefab;
            muzzleFlash = flash;
            impact = hit;
            fireCue = fire;
        }
    }
}
