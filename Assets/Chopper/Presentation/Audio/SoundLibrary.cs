using UnityEngine;
using UnityEngine.Audio;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// Every sound the game code asks for, in one asset. Code asks for "the gun fire sound", designers pick the clips.
    /// Any cue can be empty: that sound is then not played. Made by menu Chopper → Audio → Add Audio To Open Scene.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Audio/Sound Library", fileName = "SoundLibrary")]
    public sealed class SoundLibrary : ScriptableObject
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
