using Chopper.Simulation.Combat;
using Chopper.Simulation.Session;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Match
{
    /// <summary>
    /// What one player did in one match. The server counts it (<see cref="MatchStatsSystem"/>) from the gameplay event
    /// stream (<see cref="MatchLogEntry"/>) and sends it to every client at match end (<see cref="MatchStatsRpc"/>).
    /// Clients only read it: end-of-match awards and XP.
    /// </summary>
    public struct PlayerMatchStats
    {
        public int Kills;
        /// <summary>Those of <see cref="Kills"/> that were server bots (they give less XP).</summary>
        public int BotKills;
        public int Deaths;
        /// <summary>Gun rounds, rockets and missiles fired.</summary>
        public int Shots;
        /// <summary>Gun and blast hits on others (not damage over time, not rams).</summary>
        public int Hits;
        public int BestStreak;
        /// <summary>Deaths by a crash (walls, roofs, the ground, a ram).</summary>
        public int Crashes;
        /// <summary>Deaths outside the arena.</summary>
        public int BoundsDeaths;
        /// <summary>Times this player rammed another chopper.</summary>
        public int Rams;
        /// <summary>Missiles that blew up at their target.</summary>
        public int MissileHits;
        /// <summary>Enemy heat seekers this player's flares pulled away.</summary>
        public int MissilesDodged;
        public int Flares;
        /// <summary>Field repairs finished or broken off (each hold of the key that mended something).</summary>
        public int Repairs;
        public int Abilities;
        public float DamageDealt;
        public float DamageTaken;
        /// <summary>Health mended by field repair.</summary>
        public float Repaired;
        /// <summary>Metres to the victim of the longest kill.</summary>
        public float LongestKill;
        /// <summary>This player made the first kill of the match.</summary>
        public bool FirstBlood;

        /// <summary>Hits per shot, 0..1 (rockets can hit more than one target).</summary>
        public float Accuracy => Shots > 0 ? math.saturate((float)Hits / Shots) : 0f;
    }

    /// <summary>Server only, on a connection entity next to <see cref="PlayerScore"/>.</summary>
    public struct PlayerStats : IComponentData
    {
        public PlayerMatchStats Value;
        /// <summary>Kills since the last death.</summary>
        public int Streak;
        /// <summary>Server time of the last ram counted: one contact makes many hits.</summary>
        public double LastRamTime;
    }

    /// <summary>How events change the stats. Pure, so edit-mode tests can run it (MatchStatsTests).</summary>
    public static class MatchStatsRules
    {
        /// <summary>Hits by the same rammer closer together than this are one ram.</summary>
        public const double RamGapSeconds = 1.0;
        /// <summary>A missile that ends this close to its target counts as a hit.</summary>
        public const float MissileHitMetres = 15f;

        /// <summary>
        /// Applies one event to the actor's stats (<paramref name="actor"/>, network id <paramref name="actorId"/>) and to
        /// the target's (<paramref name="target"/>). Either may be the same struct as the other only for self events;
        /// pass <paramref name="hasActor"/> / <paramref name="hasTarget"/> false when that player is not known.
        /// </summary>
        public static void Apply(in MatchLogEntry entry, ref PlayerStats actor, bool hasActor, ref PlayerStats target, bool hasTarget,
            ref bool firstBloodTaken)
        {
            var other = entry.Actor != 0 && entry.Actor != entry.Target;
            switch (entry.Kind)
            {
                case MatchLogKind.Shot:
                    if (hasActor) actor.Value.Shots++;
                    break;

                case MatchLogKind.Hit:
                    if (hasTarget) target.Value.DamageTaken += entry.Amount;
                    if (!other || !hasActor) break;
                    actor.Value.DamageDealt += entry.Amount;
                    if (entry.Damage is DamageKind.Bullet or DamageKind.Explosion) actor.Value.Hits++;
                    if (entry.Damage == DamageKind.Impact && entry.Time - actor.LastRamTime >= RamGapSeconds)
                    {
                        actor.Value.Rams++;
                        actor.LastRamTime = entry.Time;
                    }
                    break;

                case MatchLogKind.Kill:
                    if (hasTarget)
                    {
                        target.Value.Deaths++;
                        target.Streak = 0;
                        // The killing blow, not the credit: a chased chopper that hit a wall still crashed.
                        if (entry.Damage is DamageKind.Impact or DamageKind.Landing) target.Value.Crashes++;
                        if (entry.Damage == DamageKind.Bounds) target.Value.BoundsDeaths++;
                    }
                    if (!other || !hasActor) break;
                    actor.Value.Kills++;
                    if (Bots.BotIds.IsBot(entry.Target)) actor.Value.BotKills++;
                    actor.Streak++;
                    actor.Value.BestStreak = math.max(actor.Value.BestStreak, actor.Streak);
                    if (entry.Distance > actor.Value.LongestKill) actor.Value.LongestKill = entry.Distance;
                    if (!firstBloodTaken)
                    {
                        firstBloodTaken = true;
                        actor.Value.FirstBlood = true;
                    }
                    break;

                case MatchLogKind.Flare:
                    if (hasActor) actor.Value.Flares++;
                    break;

                case MatchLogKind.MissileDecoyed:
                    if (hasTarget) target.Value.MissilesDodged++;
                    break;

                case MatchLogKind.MissileEnd:
                    if (hasActor && entry.Target != 0 && entry.Detail != (byte)MissileOutcome.Expired &&
                        entry.Distance >= 0f && entry.Distance <= MissileHitMetres)
                        actor.Value.MissileHits++;
                    break;

                case MatchLogKind.Repair:
                    if (!hasActor || entry.Amount <= 0f) break;
                    actor.Value.Repairs++;
                    actor.Value.Repaired += entry.Amount;
                    break;

                case MatchLogKind.Ability:
                    if (hasActor) actor.Value.Abilities++;
                    break;
            }
        }
    }

    /// <summary>One player's stats, to every client when the match ends. Flat fields: RPC code generation.</summary>
    public struct MatchStatsRpc : IRpcCommand
    {
        public int NetworkId;
        public int Kills;
        public int BotKills;
        public int Deaths;
        public int Shots;
        public int Hits;
        public int BestStreak;
        public int Crashes;
        public int BoundsDeaths;
        public int Rams;
        public int MissileHits;
        public int MissilesDodged;
        public int Flares;
        public int Repairs;
        public int Abilities;
        public float DamageDealt;
        public float DamageTaken;
        public float Repaired;
        public float LongestKill;
        public byte FirstBlood;

        public static MatchStatsRpc From(int networkId, in PlayerMatchStats s) => new()
        {
            NetworkId = networkId,
            Kills = s.Kills,
            BotKills = s.BotKills,
            Deaths = s.Deaths,
            Shots = s.Shots,
            Hits = s.Hits,
            BestStreak = s.BestStreak,
            Crashes = s.Crashes,
            BoundsDeaths = s.BoundsDeaths,
            Rams = s.Rams,
            MissileHits = s.MissileHits,
            MissilesDodged = s.MissilesDodged,
            Flares = s.Flares,
            Repairs = s.Repairs,
            Abilities = s.Abilities,
            DamageDealt = s.DamageDealt,
            DamageTaken = s.DamageTaken,
            Repaired = s.Repaired,
            LongestKill = s.LongestKill,
            FirstBlood = (byte)(s.FirstBlood ? 1 : 0),
        };

        public PlayerMatchStats ToStats() => new()
        {
            Kills = Kills,
            BotKills = BotKills,
            Deaths = Deaths,
            Shots = Shots,
            Hits = Hits,
            BestStreak = BestStreak,
            Crashes = Crashes,
            BoundsDeaths = BoundsDeaths,
            Rams = Rams,
            MissileHits = MissileHits,
            MissilesDodged = MissilesDodged,
            Flares = Flares,
            Repairs = Repairs,
            Abilities = Abilities,
            DamageDealt = DamageDealt,
            DamageTaken = DamageTaken,
            Repaired = Repaired,
            LongestKill = LongestKill,
            FirstBlood = FirstBlood != 0,
        };
    }
}
