using System;
using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// The game's music (owner, 29 Oct): the identity track for the hangar and menus (the old main menu's "Heavy
    /// Riff 1"), and a match playlist that blends metal and dark synth. A scene's <see cref="Bridge.PresentationBridge"/>
    /// picks the set; the audio service plays the playlist shuffled, whole tracks, cross-faded.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Audio/Music Library", fileName = "MusicLibrary")]
    public sealed class MusicLibrary : ScriptableObject, IValidatedAsset
    {
        /// <summary>Which music a scene plays.</summary>
        public enum Set
        {
            Match = 0,
            Menu = 1,
        }

        [SerializeField, Tooltip("Identity track: hangar and menus. Loops.")] private AudioClip menu;
        [SerializeField, Tooltip("Matches: shuffled, each track plays to its end, then cross-fades to the next.")]
        private AudioClip[] match = Array.Empty<AudioClip>();
        [SerializeField, Tooltip("Seconds of cross-fade between playlist tracks.")] private float crossFadeSeconds = 3f;
        [SerializeField, Tooltip("Seconds of cross-fade when a scene change starts other music (hangar → match).")]
        private float sceneCrossFadeSeconds = 2.5f;

        [Header("Music level (on top of the player's music volume)")]
        [SerializeField, Range(0f, 1f), Tooltip("Hangar and menus.")] private float menuLevel = 1f;
        [SerializeField, Range(0f, 1f), Tooltip("In a match while you fly: low enough that the rotors, guns and hits come through.")]
        private float combatLevel = 0.4f;
        [SerializeField, Range(0f, 1f), Tooltip("In a match before you spawn, while you wait to respawn, and on the results.")]
        private float idleLevel = 0.8f;
        [SerializeField, Min(0f), Tooltip("Seconds to move between levels (spawn, death, match end).")]
        private float levelFadeSeconds = 4f;

        public AudioClip Menu => menu;
        public IReadOnlyList<AudioClip> Match => match;
        public float CrossFadeSeconds => crossFadeSeconds;
        public float SceneCrossFadeSeconds => sceneCrossFadeSeconds;
        public float MenuLevel => menuLevel;
        public float CombatLevel => combatLevel;
        public float IdleLevel => idleLevel;
        public float LevelFadeSeconds => levelFadeSeconds;

        public void CollectProblems(List<string> problems)
        {
            if (menu == null) problems.Add($"{name}: no menu (identity) track.");
            if (match == null || match.Length == 0) problems.Add($"{name}: the match playlist is empty.");
            else if (Array.IndexOf(match, null) >= 0) problems.Add($"{name}: the match playlist has an empty entry.");
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(AudioClip menuTrack, AudioClip[] matchPlaylist)
        {
            menu = menuTrack;
            match = matchPlaylist;
        }
    }
}
