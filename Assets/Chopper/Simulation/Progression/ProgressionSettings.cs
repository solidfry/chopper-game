using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Simulation.Progression
{
    /// <summary>
    /// Progression numbers (plan section 6a-1): XP rates and one level curve per track. On the game config; Setup
    /// step 1 makes it. Written without the editor (13 Nov 2026).
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Progression Settings", fileName = "ProgressionSettings")]
    public sealed class ProgressionSettings : ScriptableObject, IValidatedAsset
    {
        [SerializeField, Tooltip("XP for the parts of a match.")] private XpRates xp = XpRates.Default;
        [SerializeField, Tooltip("Account level: all match XP.")] private LevelCurve account = LevelCurve.Account;
        [SerializeField, Tooltip("Mastery of one chassis class: match XP by flight time on that class.")]
        private LevelCurve chassis = LevelCurve.Chassis;
        [SerializeField, Tooltip("Mastery of one pilot: match XP by flight time with that pilot.")]
        private LevelCurve pilot = LevelCurve.Pilot;

        [SerializeField, Tooltip("Off (while the game is in test): the hangar shows the levels but lets you fit everything. " +
                                 "On: the hangar locks by account level and mastery, and the server takes locked items out of a loadout " +
                                 "(press Setup 4 on each gameplay subscene so the spawner has these settings).")]
        private bool locksOn;

        public bool LocksOn => locksOn;
        public XpRates Xp => xp;
        public LevelCurve Account => account;
        public LevelCurve Chassis => chassis;
        public LevelCurve Pilot => pilot;

        /// <summary>For the spawner's baker: what the server needs.</summary>
        public ProgressionRules ToRules() => new() { Account = account, Chassis = chassis, Pilot = pilot, LocksOn = locksOn };

        public LevelCurve Curve(ProgressTrack track) => track switch
        {
            ProgressTrack.Chassis => chassis,
            ProgressTrack.Pilot => pilot,
            _ => account,
        };

        public void CollectProblems(List<string> problems)
        {
            if (xp.BotKillFactor < 0f || xp.BotKillFactor > 1f) problems.Add($"{name}: the bot kill factor must be from 0 to 1.");
            if (xp.MatchComplete < 0 || xp.Victory < 0 || xp.PerKill < 0 || xp.PerDamage < 0f)
                problems.Add($"{name}: XP rates must not be below 0.");
            account.CollectProblems(name, "account", problems);
            chassis.CollectProblems(name, "chassis", problems);
            pilot.CollectProblems(name, "pilot", problems);
        }
    }
}
