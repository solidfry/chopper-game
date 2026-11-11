using System;
using System.Collections.Generic;
using Chopper.Simulation.Match;

namespace Chopper.Presentation.Results
{
    /// <summary>How an award reads on its card: a feat, a joke, or a public shaming.</summary>
    public enum AwardTone : byte
    {
        Glory = 0,
        Funny = 1,
        Shame = 2,
    }

    public readonly struct Award
    {
        public readonly string Title;
        /// <summary>One short line under the title ("5 kills in one life.").</summary>
        public readonly string Line;
        public readonly AwardTone Tone;
        public readonly int Xp;
        /// <summary>Lower first: rare feats lead the card.</summary>
        public readonly int Rank;

        public Award(string title, string line, AwardTone tone, int xp, int rank)
        {
            Title = title;
            Line = line;
            Tone = tone;
            Xp = xp;
            Rank = rank;
        }
    }

    /// <summary>
    /// End-of-match awards in the Halo style, from the server's stats (<see cref="PlayerMatchStats"/>). Pure: every
    /// client gets the same stats, so every client shows the same awards (edit-mode tests: MatchAwardsTests).
    /// Each player gets the best <see cref="MaxGlory"/> feats plus one joke or shame; a player with nothing gets the
    /// participation trophy.
    /// </summary>
    public static class MatchAwards
    {
        public const int MaxGlory = 3;
        public const int MaxFunny = 1;

        private readonly struct Rule
        {
            public readonly string Title;
            public readonly AwardTone Tone;
            public readonly int Xp;
            public readonly string Description;
            public readonly Func<int, IReadOnlyDictionary<int, PlayerMatchStats>, string> Test;

            public Rule(string title, AwardTone tone, int xp, string description, Func<int, IReadOnlyDictionary<int, PlayerMatchStats>, string> test)
            {
                Title = title;
                Tone = tone;
                Xp = xp;
                Description = description;
                Test = test;
            }
        }

        /// <summary>In rank order: the first rules are the rarest. A rule returns its line, or null.</summary>
        private static readonly Rule[] Rules =
        {
            new("FLAWLESS", AwardTone.Glory, 300, "5 or more kills without dying.", (id, all) => Of(all, id) is { Deaths: 0, Kills: >= 5 } ? "5+ kills. Never went down." : null),
            new("ACE", AwardTone.Glory, 250, "5 kills in one life.", (id, all) => Of(all, id).BestStreak >= 5 ? $"{Of(all, id).BestStreak} kills in one life." : null),
            new("TOP GUN", AwardTone.Glory, 150, "The most kills in the match.", (id, all) => IsTop(all, id, s => s.Kills, 1) ? "Most kills in the match." : null),
            new("UNTOUCHABLE", AwardTone.Glory, 150, "At least one kill, never shot down.",
                (id, all) => Of(all, id) is { Deaths: 0, Kills: >= 1 and < 5 } ? "Not shot down once." : null),
            new("KILLING SPREE", AwardTone.Glory, 100, "3 kills in one life.",
                (id, all) => Of(all, id).BestStreak is >= 3 and < 5 ? $"{Of(all, id).BestStreak} kills in one life." : null),
            new("FIRST BLOOD", AwardTone.Glory, 100, "The first kill of the match.", (id, all) => Of(all, id).FirstBlood ? "Drew the first kill." : null),
            new("DEADEYE", AwardTone.Glory, 120, "Half your shots on target (30+ shots).",
                (id, all) => Of(all, id) is { Shots: >= 30 } s && s.Accuracy >= 0.5f ? $"{s.Accuracy:0%} of shots on target." : null),
            new("LONG SHOT", AwardTone.Glory, 100, "A kill from 400 m or more.",
                (id, all) => Of(all, id).LongestKill >= 400f ? $"A kill from {Of(all, id).LongestKill:0} m." : null),
            new("WRECKING CREW", AwardTone.Glory, 100, "The most damage in the match.",
                (id, all) => IsTop(all, id, s => s.DamageDealt, 100f) ? $"Most damage: {Of(all, id).DamageDealt:0}." : null),
            new("HEAT SEEKER", AwardTone.Glory, 80, "3 missiles on target.",
                (id, all) => Of(all, id).MissileHits >= 3 ? $"{Of(all, id).MissileHits} missiles on target." : null),
            new("NOT TODAY", AwardTone.Glory, 80, "Flare off 2 missiles.",
                (id, all) => Of(all, id).MissilesDodged >= 2 ? $"Flared off {Of(all, id).MissilesDodged} missiles." : null),
            new("GREASE MONKEY", AwardTone.Glory, 60, "The most hull mended in the field.",
                (id, all) => IsTop(all, id, s => s.Repaired, 100f) ? $"Patched {Of(all, id).Repaired:0} hull in the field." : null),
            new("POWER TRIP", AwardTone.Glory, 50, "Use your ability 5 times.",
                (id, all) => Of(all, id).Abilities >= 5 ? $"Ability used {Of(all, id).Abilities} times." : null),

            new("DEMOLITION DERBY", AwardTone.Funny, 40, "Ram 3 choppers.",
                (id, all) => Of(all, id).Rams >= 3 ? $"Rammed {Of(all, id).Rams} choppers. On purpose?" : null),
            new("PIÑATA", AwardTone.Shame, 25, "The most deaths (3+).",
                (id, all) => IsTop(all, id, s => s.Deaths, 3) ? "Most deaths. Everyone got a turn." : null),
            new("GRAVITY WINS", AwardTone.Shame, 25, "Crash twice.",
                (id, all) => Of(all, id).Crashes >= 2 ? $"Crashed {Of(all, id).Crashes} times. The ground is undefeated." : null),
            new("TOURIST", AwardTone.Shame, 10, "Die outside the arena.",
                (id, all) => Of(all, id).BoundsDeaths >= 1 ? "Left the arena. Did not come back." : null),
            new("SPRAY AND PRAY", AwardTone.Funny, 25, "60+ shots, under 10% hit.",
                (id, all) => Of(all, id) is { Shots: >= 60 } s && s.Accuracy < 0.1f ? $"{s.Shots} shots, {s.Accuracy:0%} hit." : null),
            new("FIREWORKS SHOW", AwardTone.Funny, 15, "15+ flares with no missile to beat.",
                (id, all) => Of(all, id) is { Flares: >= 15, MissilesDodged: 0 } s ? $"{s.Flares} flares. No missiles." : null),
            new("SPONGE", AwardTone.Funny, 25, "The most damage taken.",
                (id, all) => IsTop(all, id, s => s.DamageTaken, 200f) ? $"Soaked up {Of(all, id).DamageTaken:0} damage." : null),
            new("PACIFIST", AwardTone.Funny, 25, "No kills, no damage, barely a shot.",
                (id, all) => Of(all, id) is { Kills: 0, Shots: < 10 } s && s.DamageDealt < 1f ? "Made no enemies. Made no friends." : null),
        };

        public const string ParticipationTitle = "PARTICIPATION TROPHY";

        /// <summary>Every award there is, in rank order, with a fixed description: the profile's award case.</summary>
        public static IEnumerable<(string Title, AwardTone Tone, string Description)> Catalogue()
        {
            foreach (var rule in Rules) yield return (rule.Title, rule.Tone, rule.Description);
            yield return (ParticipationTitle, AwardTone.Funny, "Nothing else to show for the match.");
        }

        /// <summary>The awards for <paramref name="networkId"/>, best first, into <paramref name="into"/> (cleared).</summary>
        public static void For(int networkId, IReadOnlyDictionary<int, PlayerMatchStats> all, List<Award> into)
        {
            into.Clear();
            if (!all.ContainsKey(networkId)) return;
            var glory = 0;
            var funny = 0;
            for (var rank = 0; rank < Rules.Length; rank++)
            {
                var rule = Rules[rank];
                var isGlory = rule.Tone == AwardTone.Glory;
                if (isGlory ? glory >= MaxGlory : funny >= MaxFunny) continue;
                var line = rule.Test(networkId, all);
                if (line == null) continue;
                into.Add(new Award(rule.Title, line, rule.Tone, rule.Xp, rank));
                if (isGlory) glory++;
                else funny++;
            }
            if (into.Count == 0)
                into.Add(new Award(ParticipationTitle, "You were there. That counts.", AwardTone.Funny, 10, Rules.Length));
        }

        private static PlayerMatchStats Of(IReadOnlyDictionary<int, PlayerMatchStats> all, int id) =>
            all.TryGetValue(id, out var stats) ? stats : default;

        /// <summary>
        /// Strictly the most of all players, at least <paramref name="minimum"/>, and with someone to beat: a match
        /// alone (training) gives no "most" awards.
        /// </summary>
        private static bool IsTop(IReadOnlyDictionary<int, PlayerMatchStats> all, int id, Func<PlayerMatchStats, float> value, float minimum)
        {
            if (all.Count < 2) return false;
            var own = value(Of(all, id));
            if (own < minimum) return false;
            foreach (var (other, stats) in all)
                if (other != id && value(stats) >= own) return false;
            return true;
        }
    }
}
