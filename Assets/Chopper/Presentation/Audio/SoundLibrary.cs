using System.Collections.Generic;
using Chopper.Simulation.Config;
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

        [SerializeField, Tooltip("A module locks onto its socket.")] private SoundCue moduleFit;
        [SerializeField, Tooltip("A socket is emptied.")] private SoundCue moduleRemove;
        [SerializeField, Tooltip("The camera moves to a slot or back to the overview.")] private SoundCue slotFocus;
        [SerializeField, Tooltip("A new chassis scans in.")] private SoundCue chassisScan;

        public AudioMixer Mixer => mixer;
        public AudioMixerGroup MusicGroup => musicGroup;
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
        public void InitialiseRepair(SoundCue loop, SoundCue done, SoundCue interrupted)
        {
            repairLoop = loop;
            repairDone = done;
            repairInterrupted = interrupted;
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
