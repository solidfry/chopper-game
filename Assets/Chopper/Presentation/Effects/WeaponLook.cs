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
        [SerializeField, Tooltip("Guns: a looping fire sound played while the gun fires, in place of the Fire Cue per shot " +
                                 "(a minigun's continuous tear). The Tail Cue plays when it stops.")]
        private SoundCue fireLoop;
        [SerializeField, Tooltip("Guns with spin-up: the motor whine as the barrels start.")]
        private SoundCue spinUpCue;
        [SerializeField, Tooltip("Guns with spin-up: the whine as the barrels run down.")]
        private SoundCue spinDownCue;
        [SerializeField, Tooltip("Guns: the shot's echo in open air (owner 27 Nov: shots felt flat next to blasts). Played " +
                                 "with a shot; the cue's cooldown keeps a burst to a rolling echo. Empty = none.")]
        private SoundCue tailCue;
        [SerializeField, Tooltip("Rockets, bombs, mines: a looping sound that follows the round in flight (bomb whistle). " +
                                 "Empty = none (the projectile prefab's own sound plays).")]
        private SoundCue flightLoop;
        [SerializeField, Tooltip("Rockets: plays once when a drifting round stops and hangs (an air mine arms). Empty = none.")]
        private SoundCue armCue;
        [SerializeField, Tooltip("Rockets: the round's model grows with the blast radius (one model reads light or heavy). " +
                                 "Off for a family with its own true-size model (bomb, mine).")]
        private bool scaleWithBlast = true;
        [SerializeField, Tooltip("No flash at launch (a bomb or mine is dropped, not fired).")]
        private bool noMuzzleFlash;

        [Header("Cluster missiles")]
        [SerializeField, Tooltip("Bomblet model after the split. Empty = the shared rocket.")]
        private GameObject bomblet;
        [SerializeField, Tooltip("Flash where the carrier splits (once per split).")]
        private GameObject splitFlash;
        [SerializeField, Tooltip("Sound where the carrier splits (once per split).")]
        private SoundCue splitCue;
        [SerializeField, Range(0f, 0.5f), Tooltip("Camera shake added per shot of your own gun (trauma, 0..1; the shake " +
                                                  "grows with its square). 0 = none.")]
        private float cameraKick;
        [SerializeField, Range(0.1f, 2f), Tooltip("Guns: tracer size (1 = the prefab's own; owner 27 Nov: the pack's tracers " +
                                                  "read too fat). Rockets use Scale With Blast instead.")]
        private float tracerScale = 1f;
        [SerializeField, Tooltip("Rockets: realistic blast layers spawned with the impact (the wreck blast's parts: fireball, " +
                                 "embers, smoke). Delays are ignored: all start at the hit. Owner 27 Nov: heavy rockets did not " +
                                 "look dangerous.")]
        private BlastLayer[] impactLayers = System.Array.Empty<BlastLayer>();
        [SerializeField, Range(0f, 2f), Tooltip("Guns: seconds the round's trail stays after it hits (owner 27 Nov: the railgun " +
                                                "tail should hang for about half a second). 0 = gone at once.")]
        private float trailLinger;
        [SerializeField, Range(0.5f, 4f), Tooltip("Guns: tracer colour and opacity, times the prefab's (1 = as made; owner " +
                                                  "27 Nov: the pack's tracers read too faint).")]
        private float tracerBrightness = 1f;

        public GameObject Projectile => projectile;
        public float CameraKick => cameraKick;
        public float TracerScale => tracerScale;
        public System.Collections.Generic.IReadOnlyList<BlastLayer> ImpactLayers => impactLayers ?? System.Array.Empty<BlastLayer>();

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetImpactLayers(BlastLayer[] layers) => impactLayers = layers ?? System.Array.Empty<BlastLayer>();
        public float TrailLinger => trailLinger;
        public float TracerBrightness => tracerBrightness;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetTracerBrightness(float brightness) => tracerBrightness = brightness;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetTracerScale(float scale) => tracerScale = scale;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetTrailLinger(float seconds) => trailLinger = seconds;
        public GameObject MuzzleFlash => muzzleFlash;
        public GameObject Impact => impact;
        public SoundCue FireCue => fireCue;
        public SoundCue FireLoop => fireLoop;
        public SoundCue SpinUpCue => spinUpCue;
        public SoundCue SpinDownCue => spinDownCue;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetSpin(SoundCue loop, SoundCue up, SoundCue down)
        {
            fireLoop = loop;
            spinUpCue = up;
            spinDownCue = down;
        }
        public SoundCue TailCue => tailCue;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetTail(SoundCue cue) => tailCue = cue;
        public SoundCue FlightLoop => flightLoop;
        public SoundCue ArmCue => armCue;

        /// <summary>
        /// Rockets: the look brings its own launch or flight sound, so the projectile prefab's own sounds (the pack's launch
        /// and engine loop, one per round with no voice limit) are muted. A salvo then plays as a ripple, not a pile-up.
        /// </summary>
        public bool OwnsRoundSound => fireCue != null || flightLoop != null;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetArmCue(SoundCue cue) => armCue = cue;
        public bool ScaleWithBlast => scaleWithBlast;
        public bool NoMuzzleFlash => noMuzzleFlash;
        public GameObject Bomblet => bomblet;
        public GameObject SplitFlash => splitFlash;
        public SoundCue SplitCue => splitCue;

        public void CollectProblems(List<string> problems)
        {
            // Every slot may be empty (falls back); nothing to check yet.
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetCluster(GameObject bombletModel, GameObject flash, SoundCue cue)
        {
            bomblet = bombletModel;
            splitFlash = flash;
            splitCue = cue;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(GameObject projectilePrefab, GameObject flash, GameObject hit, SoundCue fire, float kick = 0f,
            SoundCue flight = null, bool scaleModel = true, bool dropped = false)
        {
            scaleWithBlast = scaleModel;
            noMuzzleFlash = dropped;
            flightLoop = flight;
            cameraKick = kick;
            projectile = projectilePrefab;
            muzzleFlash = flash;
            impact = hit;
            fireCue = fire;
        }
    }
}
