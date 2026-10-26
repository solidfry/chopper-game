using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;
using UnityEngine.Audio;

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
        [SerializeField] private SoundCue dash;

        [Header("Combat")]
        [SerializeField] private SoundCue gunFire;
        [SerializeField] private SoundCue hitConfirm;
        [SerializeField] private SoundCue killConfirm;
        [SerializeField] private SoundCue damageTaken;
        [SerializeField, Tooltip("Loop while outside the arena.")] private SoundCue outOfBoundsAlarm;

        [Header("Match")]
        [SerializeField] private SoundCue matchEnd;

        [Header("UI")]
        [SerializeField] private SoundCue uiClick;
        [SerializeField] private SoundCue uiNavigate;

        public AudioMixer Mixer => mixer;
        public AudioMixerGroup MusicGroup => musicGroup;
        public SoundCue RotorLocal => rotorLocal;
        public SoundCue RotorRemote => rotorRemote;
        public SoundCue Dash => dash;
        public SoundCue GunFire => gunFire;
        public SoundCue HitConfirm => hitConfirm;
        public SoundCue KillConfirm => killConfirm;
        public SoundCue DamageTaken => damageTaken;
        public SoundCue OutOfBoundsAlarm => outOfBoundsAlarm;
        public SoundCue MatchEnd => matchEnd;
        public SoundCue UIClick => uiClick;
        public SoundCue UINavigate => uiNavigate;

        public void CollectProblems(List<string> problems)
        {
            if (mixer == null) problems.Add($"{name}: no audio mixer.");
            if (musicGroup == null) problems.Add($"{name}: no music mixer group.");
            Check(rotorLocal, nameof(rotorLocal), problems);
            Check(rotorRemote, nameof(rotorRemote), problems);
            Check(dash, nameof(dash), problems);
            Check(gunFire, nameof(gunFire), problems);
            Check(hitConfirm, nameof(hitConfirm), problems);
            Check(killConfirm, nameof(killConfirm), problems);
            Check(damageTaken, nameof(damageTaken), problems);
            Check(outOfBoundsAlarm, nameof(outOfBoundsAlarm), problems);
            Check(matchEnd, nameof(matchEnd), problems);
            Check(uiClick, nameof(uiClick), problems);
            Check(uiNavigate, nameof(uiNavigate), problems);
        }

        private void Check(SoundCue cue, string slot, List<string> problems)
        {
            if (cue == null) problems.Add($"{name}: no cue for {slot} (that sound will not play).");
            else cue.CollectProblems(problems);
        }

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void Initialise(AudioMixer audioMixer, AudioMixerGroup music, SoundCue localRotor, SoundCue remoteRotor, SoundCue dashCue,
            SoundCue gun, SoundCue hit, SoundCue kill, SoundCue damage, SoundCue alarm, SoundCue end, SoundCue click, SoundCue navigate)
        {
            mixer = audioMixer;
            musicGroup = music;
            rotorLocal = localRotor;
            rotorRemote = remoteRotor;
            dash = dashCue;
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
