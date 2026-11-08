using System;
using System.Collections.Generic;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Simulation.Loadout
{
    /// <summary>
    /// What a pilot changes (plan section 5b). Multipliers: 1 = no change. Applied by <see cref="LoadoutBuilder"/> (one
    /// code path for the baker and for runtime): the flight, hull, repair and weapon numbers change on the chopper's own
    /// configs; the combat rules (damage taken, damage over time, ram, kill heal, jam) go on <see cref="PilotTraits"/>.
    /// </summary>
    [Serializable]
    public sealed class PilotModifiers
    {
        [Header("Hull and flight")]
        public float HealthMultiplier = 1f;
        public float TurnRateMultiplier = 1f;
        [Tooltip("Collective lift (climb).")] public float LiftMultiplier = 1f;
        [Tooltip("Boost tank refill time (below 1 = refills faster).")] public float BoostRechargeMultiplier = 1f;

        [Header("Repair")]
        [Tooltip("Repair time (below 1 = faster).")] public float RepairTimeMultiplier = 1f;
        [Tooltip("Repair lock after damage.")] public float RepairLockMultiplier = 1f;

        [Header("Weapons")]
        public float GunDamageMultiplier = 1f;
        [Tooltip("Own seeker lock time (below 1 = locks faster).")] public float LockTimeMultiplier = 1f;
        [Tooltip("Rounds per pylon magazine.")] public float MagazineMultiplier = 1f;
        [Tooltip("Gun hits corrode: this share of each hit again as damage over time.")] public float BulletDotShare;
        public float BulletDotSeconds = 3f;
        [Tooltip("Rocket and missile hits burn: this share of each hit again as damage over time.")] public float ExplosiveDotShare;
        public float ExplosiveDotSeconds = 4f;
        [Tooltip("Your hits glitch the victim's HUD.")] public bool JamOnHit;

        [Header("Defence")]
        [Tooltip("All damage taken.")] public float DamageTakenMultiplier = 1f;
        [Tooltip("Crash and hard-landing damage taken.")] public float CrashDamageMultiplier = 1f;
        [Tooltip("Enemy seekers take this much longer to lock you.")] public float EnemyLockTimeMultiplier = 1f;
        [Tooltip("Heat signature: seekers prefer hotter targets.")] public float HeatMultiplier = 1f;

        [Header("Other")]
        [Tooltip("Damage your rams do to the other chopper.")] public float RamDamageMultiplier = 1f;
        [Tooltip("A kill gives back this share of your hull.")] public float KillHeal;

        public PilotTraits ToTraits() => new()
        {
            DamageTakenScale = DamageTakenMultiplier,
            CrashTakenScale = CrashDamageMultiplier,
            RamDealtScale = RamDamageMultiplier,
            KillHeal = KillHeal,
            BulletDotShare = BulletDotShare,
            BulletDotSeconds = BulletDotSeconds,
            ExplosiveDotShare = ExplosiveDotShare,
            ExplosiveDotSeconds = ExplosiveDotSeconds,
            JamOnHit = JamOnHit,
        };

        /// <summary>
        /// The pilot card's lines, e.g. "+40% REPAIR SPEED" (good) or "-10% GUN DAMAGE" (bad), in a fixed order. Only the
        /// numbers that differ from 1 (or from 0 for the shares).
        /// </summary>
        public List<(string Text, bool Good)> Effects()
        {
            var effects = new List<(string, bool)>();
            Percent(effects, "HULL", HealthMultiplier, higherIsGood: true);
            Percent(effects, "DAMAGE TAKEN", DamageTakenMultiplier, higherIsGood: false);
            Percent(effects, "CRASH DAMAGE", CrashDamageMultiplier, higherIsGood: false);
            // Time multipliers read as speed: 0.6 of the time = "+40% REPAIR SPEED".
            Speed(effects, "REPAIR SPEED", RepairTimeMultiplier);
            Speed(effects, "REPAIR LOCK", RepairLockMultiplier, asTime: true);
            Speed(effects, "BOOST RECHARGE", BoostRechargeMultiplier);
            Percent(effects, "TURN RATE", TurnRateMultiplier, higherIsGood: true);
            Percent(effects, "CLIMB", LiftMultiplier, higherIsGood: true);
            Percent(effects, "GUN DAMAGE", GunDamageMultiplier, higherIsGood: true);
            Speed(effects, "LOCK SPEED", LockTimeMultiplier);
            Percent(effects, "PYLON ROUNDS", MagazineMultiplier, higherIsGood: true);
            Percent(effects, "ENEMY LOCK TIME", EnemyLockTimeMultiplier, higherIsGood: true);
            Percent(effects, "HEAT SIGNATURE", HeatMultiplier, higherIsGood: false);
            Percent(effects, "RAM DAMAGE", RamDamageMultiplier, higherIsGood: true);
            if (BulletDotShare > 0f) effects.Add(($"GUN HITS CORRODE +{Mathf.RoundToInt(BulletDotShare * 100f)}% / {BulletDotSeconds:0}S", true));
            if (ExplosiveDotShare > 0f) effects.Add(($"BLASTS BURN +{Mathf.RoundToInt(ExplosiveDotShare * 100f)}% / {ExplosiveDotSeconds:0}S", true));
            if (KillHeal > 0f) effects.Add(($"KILLS REPAIR {Mathf.RoundToInt(KillHeal * 100f)}% HULL", true));
            if (JamOnHit) effects.Add(("HITS JAM ENEMY HUD", true));
            return effects;
        }

        private static void Percent(List<(string, bool)> effects, string label, float multiplier, bool higherIsGood)
        {
            var percent = Mathf.RoundToInt((multiplier - 1f) * 100f);
            if (percent == 0) return;
            effects.Add(($"{(percent > 0 ? "+" : "")}{percent}% {label}", percent > 0 == higherIsGood));
        }

        /// <summary>A time multiplier as "+x% faster" (time 0.6 = +40%), or as time itself ("-50% REPAIR LOCK").</summary>
        private static void Speed(List<(string, bool)> effects, string label, float timeMultiplier, bool asTime = false)
        {
            var percent = Mathf.RoundToInt((asTime ? timeMultiplier - 1f : 1f - timeMultiplier) * 100f);
            if (percent == 0) return;
            effects.Add(($"{(percent > 0 ? "+" : "")}{percent}% {label}", asTime ? percent < 0 : percent > 0));
        }
    }

    /// <summary>The numbers of a pilot's active ability (plan section 5b, step 4). See <see cref="AbilityKind"/>.</summary>
    [Serializable]
    public sealed class PilotAbilitySettings
    {
        public AbilityKind Kind;
        [Min(0f)] public float Cooldown = 30f;
        [Min(0f), Tooltip("Seconds it stays on.")] public float Duration = 4f;
        [Tooltip("Nanite Cloud: hull share healed. Venom Belt / Napalm Salvo: damage-over-time scale. Aegis: shield points. Blackout: jam seconds.")]
        public float Strength = 1f;
        [Tooltip("Blackout: reach (m).")] public float Radius = 150f;
        [Tooltip("When a bot pilot uses it. Auto = the default for the kind.")] public Bots.BotUse Bot;

        public AbilityConfig ToConfig() => new()
        {
            Kind = Kind,
            Cooldown = Cooldown,
            Duration = Duration,
            Strength = Strength,
            Radius = Radius,
            Bot = Bot,
        };
    }

    /// <summary>
    /// A pilot (plan section 5b): a call sign that says what they do (never a personal name), one passive
    /// (<see cref="PilotModifiers"/>, with a small cost) and one active ability (<see cref="PilotAbilitySettings"/>,
    /// key F or D-pad up). Any pilot flies any chassis. Listed in the <see cref="LoadoutCatalog"/>; the id travels in the loadout.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Loadout/Pilot", fileName = "Pilot")]
    public sealed class PilotDefinition : ScriptableObject, IValidatedAsset
    {
        [SerializeField, Tooltip("Unique, never 0 (0 = no pilot), never reused. Saved in player loadouts.")]
        private ushort id;
        [SerializeField, Tooltip("Upper case call sign: what the pilot does (APOTHECARY, VIPER). No personal names.")]
        private string callSign;
        [SerializeField, Tooltip("Two or three words, e.g. FIELD MEDIC.")] private string role;
        [SerializeField, TextArea, Tooltip("The passive in a sentence (sentence case).")] private string passive;
        [SerializeField] private string abilityName;
        [SerializeField, TextArea] private string abilityText;
        [SerializeField, Tooltip("Card and tile accent.")] private Color accent = new(0f, 1f, 0.58f);
        [SerializeField, Tooltip("Optional. Empty = the call sign's monogram.")] private Texture2D portrait;
        [SerializeField] private int unlockLevel;
        [SerializeField, Tooltip("The announcer says the call sign (select screen).")] private AudioClip selectLine;
        [SerializeField, Tooltip("The pilot on the radio when the ability starts (own chopper).")] private AudioClip abilityLine;
        [SerializeField] private PilotModifiers modifiers = new();
        [SerializeField, Tooltip("The active ability's kind and numbers (the text is above).")]
        private PilotAbilitySettings ability = new();

        public ushort Id => id;
        public string CallSign => callSign;
        public string Role => role;
        public string Passive => passive;
        public string AbilityName => abilityName;
        public string AbilityText => abilityText;
        public Color Accent => accent;
        public Texture2D Portrait => portrait;
        public int UnlockLevel => unlockLevel;
        public AudioClip SelectLine => selectLine;
        public AudioClip AbilityLine => abilityLine;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetVoice(AudioClip select, AudioClip abilityStart)
        {
            selectLine = select;
            abilityLine = abilityStart;
        }
        public PilotModifiers Modifiers => modifiers ??= new PilotModifiers();
        public PilotAbilitySettings Ability => ability ??= new PilotAbilitySettings();

        /// <summary>Editor only: used by the setup tool (abilities came after the first pilots).</summary>
        public void SetAbility(PilotAbilitySettings settings, string name, string text)
        {
            ability = settings;
            abilityName = name;
            abilityText = text;
        }

        public void CollectProblems(List<string> problems)
        {
            if (id == 0) problems.Add($"{name}: id must not be 0 (0 means no pilot).");
            if (string.IsNullOrWhiteSpace(callSign)) problems.Add($"{name}: no call sign.");
            else if (callSign != callSign.ToUpperInvariant()) problems.Add($"{name}: call sign should be upper case.");
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(ushort pilotId, string sign, string pilotRole, string passiveText, string ability, string abilityDescription,
            Color accentColour, PilotModifiers pilotModifiers, int level = 0)
        {
            unlockLevel = level;
            id = pilotId;
            callSign = sign;
            role = pilotRole;
            passive = passiveText;
            abilityName = ability;
            abilityText = abilityDescription;
            accent = accentColour;
            modifiers = pilotModifiers;
        }
    }
}
