using System.Collections.Generic;
using Chopper.Simulation.Config;
using Chopper.Simulation.Visuals;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Serialization;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// Every sound the game code asks for, in one asset. Code asks for "the gun fire sound", designers pick the clips.
    /// Any cue can be empty: that sound is then not played. Made by Chopper → Setup.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Audio/Sound Library", fileName = "SoundLibrary")]
    public sealed class SoundLibrary : ScriptableObject, IValidatedAsset
    {
        [SerializeField, Tooltip("Needs exposed volume parameters named <Channel>Volume (MasterVolume, MusicVolume, ...).")]
        private AudioMixer mixer;
        [SerializeField] private AudioMixerGroup musicGroup;
        [SerializeField, Tooltip("Voice lines (pilot call signs, ability radio calls). Empty = straight to the listener.")]
        private AudioMixerGroup voiceGroup;

        [Header("Chopper")]
        [SerializeField, Tooltip("Loop. Own chopper, mostly 2D (cockpit).")] private SoundCue rotorLocal;
        [SerializeField, Tooltip("Loop. Other choppers, 3D.")] private SoundCue rotorRemote;
        [SerializeField, FormerlySerializedAs("dash"), Tooltip("One-shot when a boost starts (the kick).")]
        private SoundCue boostStart;
        [SerializeField, Tooltip("Loop while boosting (the engine roar). Volume and pitch follow the boost.")]
        private SoundCue boostLoop;

        [Header("Combat")]
        [SerializeField] private SoundCue gunFire;
        [SerializeField] private SoundCue hitConfirm;
        [SerializeField] private SoundCue killConfirm;
        [SerializeField] private SoundCue damageTaken;
        [SerializeField, Tooltip("Loop while outside the arena.")] private SoundCue outOfBoundsAlarm;

        [Header("Surfaces: bullet hits (3D, where a bullet lands)")]
        [SerializeField] private SoundCue metalHit;
        [SerializeField] private SoundCue concreteHit;
        [SerializeField] private SoundCue groundHit;
        [SerializeField] private SoundCue waterHit;

        [Header("Surfaces: crashes (3D, a chopper hits something; louder for harder hits)")]
        [SerializeField] private SoundCue metalCrash;
        [SerializeField] private SoundCue concreteCrash;
        [SerializeField] private SoundCue groundCrash;
        [SerializeField] private SoundCue waterCrash;

        [Header("Cockpit muffling (each cue's Muffle): a low-pass, as if heard from inside")]
        [SerializeField, Range(200f, 22000f), Tooltip("Outside cues in a cockpit view (Hz): through the canopy. Lower = more muffled.")]
        private float cockpitCutoff = 800f;
        [SerializeField, Range(200f, 22000f), Tooltip("Cabin cues in a cockpit view (Hz): the own rotor and boost. Lower = duller.")]
        private float cabinCutoff = 1500f;
        [SerializeField, Range(200f, 22000f), Tooltip("All muffled cues in a chase view (Hz). 22000 = not muffled.")]
        private float chaseCutoff = 22000f;
        [SerializeField, Min(0.1f), Tooltip("How fast it changes between views (1/s).")]
        private float cutoffSpeed = 6f;

        [Header("Ambience")]
        [SerializeField, Tooltip("Loop under a city map, on the Ambient channel.")] private SoundCue cityAmbience;

        [Header("Targeting")]
        [SerializeField, Tooltip("Loop while a lock builds; pitch rises with progress.")] private SoundCue lockTone;
        [SerializeField, Tooltip("Loop while locked.")] private SoundCue lockedTone;
        [SerializeField, Tooltip("Loop while an enemy is locking or has locked you.")] private SoundCue lockWarning;
        [SerializeField, Tooltip("Loop while a missile chases you; it speeds up as it closes in.")] private SoundCue missileWarning;
        [SerializeField] private SoundCue flares;

        [Header("Match")]
        [SerializeField] private SoundCue matchEnd;

        [Header("UI")]
        [SerializeField] private SoundCue uiClick;
        [SerializeField] private SoundCue uiNavigate;

        [Header("Hangar")]
        [Header("Field repair (own chopper)")]
        [SerializeField, Tooltip("Loop while repairing; pitch rises as the hull fills.")] private SoundCue repairLoop;
        [SerializeField, Tooltip("The hull is full.")] private SoundCue repairDone;
        [SerializeField, Tooltip("A hit stopped the repair (it locks for a moment).")] private SoundCue repairInterrupted;

        [Header("Pilot ability (own chopper)")]
        [SerializeField, Tooltip("The ability starts.")] private SoundCue abilityActivate;
        [SerializeField, Tooltip("The cooldown is over.")] private SoundCue abilityReady;
        [SerializeField, Tooltip("A Blackout jammed you.")] private SoundCue jammed;

        [SerializeField, Tooltip("A module locks onto its socket.")] private SoundCue moduleFit;
        [SerializeField, Tooltip("A socket is emptied.")] private SoundCue moduleRemove;
        [SerializeField, Tooltip("The camera moves to a slot or back to the overview.")] private SoundCue slotFocus;
        [SerializeField, Tooltip("A new chassis scans in.")] private SoundCue chassisScan;

        public AudioMixer Mixer => mixer;
        public AudioMixerGroup MusicGroup => musicGroup;
        public AudioMixerGroup VoiceGroup => voiceGroup;

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void SetVoiceGroup(AudioMixerGroup group) => voiceGroup = group;
        public SoundCue RotorLocal => rotorLocal;
        public SoundCue RotorRemote => rotorRemote;
        public SoundCue BoostStart => boostStart;
        public SoundCue BoostLoop => boostLoop;
        public SoundCue GunFire => gunFire;
        public SoundCue HitConfirm => hitConfirm;
        public SoundCue KillConfirm => killConfirm;
        public SoundCue DamageTaken => damageTaken;
        public SoundCue OutOfBoundsAlarm => outOfBoundsAlarm;
        public SoundCue MatchEnd => matchEnd;
        public SoundCue LockTone => lockTone;
        public SoundCue LockedTone => lockedTone;
        public SoundCue LockWarning => lockWarning;
        public SoundCue MissileWarning => missileWarning;
        public SoundCue Flares => flares;
        public SoundCue UIClick => uiClick;
        public SoundCue UINavigate => uiNavigate;
        /// <summary>A bullet lands on this surface. Falls back to the concrete cue.</summary>
        public SoundCue SurfaceHit(SurfaceKind surface) => Pick(surface, metalHit, concreteHit, groundHit, waterHit);

        /// <summary>A chopper crashes into this surface. Falls back to the concrete cue.</summary>
        public SoundCue SurfaceCrash(SurfaceKind surface) => Pick(surface, metalCrash, concreteCrash, groundCrash, waterCrash);

        private static SoundCue Pick(SurfaceKind surface, SoundCue metal, SoundCue concrete, SoundCue ground, SoundCue water)
        {
            var cue = surface switch
            {
                SurfaceKind.Metal => metal,
                SurfaceKind.Ground => ground,
                SurfaceKind.Water => water,
                _ => concrete,
            };
            return cue != null ? cue : concrete;
        }

        public float CockpitCutoff => cockpitCutoff;
        public float CabinCutoff => cabinCutoff;
        public float ChaseCutoff => chaseCutoff;
        public float CutoffSpeed => cutoffSpeed;
        public SoundCue CityAmbience => cityAmbience;
        public SoundCue AbilityActivate => abilityActivate;
        public SoundCue AbilityReady => abilityReady;
        public SoundCue Jammed => jammed;
        public SoundCue RepairLoop => repairLoop;
        public SoundCue RepairDone => repairDone;
        public SoundCue RepairInterrupted => repairInterrupted;
        public SoundCue ModuleFit => moduleFit;
        public SoundCue ModuleRemove => moduleRemove;
        public SoundCue SlotFocus => slotFocus;
        public SoundCue ChassisScan => chassisScan;

        public void CollectProblems(List<string> problems)
        {
            if (mixer == null) problems.Add($"{name}: no audio mixer.");
            if (musicGroup == null) problems.Add($"{name}: no music mixer group.");
            Check(rotorLocal, nameof(rotorLocal), problems);
            Check(rotorRemote, nameof(rotorRemote), problems);
            Check(boostStart, nameof(boostStart), problems);
            Check(boostLoop, nameof(boostLoop), problems);
            Check(gunFire, nameof(gunFire), problems);
            Check(hitConfirm, nameof(hitConfirm), problems);
            Check(killConfirm, nameof(killConfirm), problems);
            Check(damageTaken, nameof(damageTaken), problems);
            Check(outOfBoundsAlarm, nameof(outOfBoundsAlarm), problems);
            Check(matchEnd, nameof(matchEnd), problems);
            Check(lockTone, nameof(lockTone), problems);
            Check(lockedTone, nameof(lockedTone), problems);
            Check(lockWarning, nameof(lockWarning), problems);
            Check(missileWarning, nameof(missileWarning), problems);
            Check(flares, nameof(flares), problems);
            Check(uiClick, nameof(uiClick), problems);
            Check(uiNavigate, nameof(uiNavigate), problems);
            Check(moduleFit, nameof(moduleFit), problems);
            Check(moduleRemove, nameof(moduleRemove), problems);
            Check(slotFocus, nameof(slotFocus), problems);
            Check(chassisScan, nameof(chassisScan), problems);
            Check(repairLoop, nameof(repairLoop), problems);
            Check(repairDone, nameof(repairDone), problems);
            Check(repairInterrupted, nameof(repairInterrupted), problems);
            Check(abilityActivate, nameof(abilityActivate), problems);
            Check(abilityReady, nameof(abilityReady), problems);
            Check(jammed, nameof(jammed), problems);
            Check(cityAmbience, nameof(cityAmbience), problems);
            Check(metalHit, nameof(metalHit), problems);
            Check(concreteHit, nameof(concreteHit), problems);
            Check(groundHit, nameof(groundHit), problems);
            Check(waterHit, nameof(waterHit), problems);
            Check(metalCrash, nameof(metalCrash), problems);
            Check(concreteCrash, nameof(concreteCrash), problems);
            Check(groundCrash, nameof(groundCrash), problems);
            Check(waterCrash, nameof(waterCrash), problems);
        }

        private void Check(SoundCue cue, string slot, List<string> problems)
        {
            if (cue == null) problems.Add($"{name}: no cue for {slot} (that sound will not play).");
            else cue.CollectProblems(problems);
        }

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void InitialiseBoost(SoundCue start, SoundCue loop)
        {
            boostStart = start;
            boostLoop = loop;
        }

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void InitialiseAbility(SoundCue activate, SoundCue ready, SoundCue jam)
        {
            abilityActivate = activate;
            abilityReady = ready;
            jammed = jam;
        }

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void InitialiseRepair(SoundCue loop, SoundCue done, SoundCue interrupted)
        {
            repairLoop = loop;
            repairDone = done;
            repairInterrupted = interrupted;
        }

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void InitialiseAmbience(SoundCue city) => cityAmbience = city;

        /// <summary>Editor only: the cue in a surface slot as set (no fallback). Order: metal, concrete, ground, water.</summary>
        public SoundCue SurfaceHitCue(int index) => index switch { 0 => metalHit, 1 => concreteHit, 2 => groundHit, _ => waterHit };

        /// <summary>Editor only: the cue in a surface slot as set (no fallback). Order: metal, concrete, ground, water.</summary>
        public SoundCue SurfaceCrashCue(int index) => index switch { 0 => metalCrash, 1 => concreteCrash, 2 => groundCrash, _ => waterCrash };

        /// <summary>Editor only: used by the audio setup tool. Order: metal, concrete, ground, water.</summary>
        public void InitialiseSurfaces(SoundCue[] hits, SoundCue[] crashes)
        {
            metalHit = hits[0];
            concreteHit = hits[1];
            groundHit = hits[2];
            waterHit = hits[3];
            metalCrash = crashes[0];
            concreteCrash = crashes[1];
            groundCrash = crashes[2];
            waterCrash = crashes[3];
        }

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void InitialiseHangar(SoundCue fit, SoundCue remove, SoundCue focus, SoundCue scan)
        {
            moduleFit = fit;
            moduleRemove = remove;
            slotFocus = focus;
            chassisScan = scan;
        }

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void InitialiseTargeting(SoundCue lockBuilding, SoundCue locked, SoundCue beingLocked, SoundCue missileInbound, SoundCue flareDrop)
        {
            lockTone = lockBuilding;
            lockedTone = locked;
            lockWarning = beingLocked;
            missileWarning = missileInbound;
            flares = flareDrop;
        }

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void Initialise(AudioMixer audioMixer, AudioMixerGroup music, SoundCue localRotor, SoundCue remoteRotor,
            SoundCue gun, SoundCue hit, SoundCue kill, SoundCue damage, SoundCue alarm, SoundCue end, SoundCue click, SoundCue navigate)
        {
            mixer = audioMixer;
            musicGroup = music;
            rotorLocal = localRotor;
            rotorRemote = remoteRotor;
            gunFire = gun;
            hitConfirm = hit;
            killConfirm = kill;
            damageTaken = damage;
            outOfBoundsAlarm = alarm;
            matchEnd = end;
            uiClick = click;
            uiNavigate = navigate;
        }
    }
}
