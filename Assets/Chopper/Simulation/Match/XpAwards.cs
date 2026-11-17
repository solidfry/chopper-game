using Chopper.Simulation.Progression;
using Chopper.Simulation.Session;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Match
{
    /// <summary>Why a player got XP during the match (the popup's title).</summary>
    public enum XpReason : byte
    {
        Kill = 0,
        BotKill = 1,
        Assist = 2,
        Repair = 3,
        MissileEvaded = 4,
    }

    /// <summary>One XP award during the match, for one player.</summary>
    public struct XpAward
    {
        public int Player;
        public XpReason Reason;
        public int Amount;
        /// <summary>The other player (the victim of a kill or assist; the missile's shooter). 0 = none.</summary>
        public int Other;
        /// <summary>Kills: the XP for the damage the killer did to this victim this life (already counted in the
        /// match's damage XP; shown under the kill).</summary>
        public int DamageXp;
    }

    /// <summary>Server to one player: an XP award just now. The client shows the popup (cosmetic; the end-of-match total
    /// comes from <see cref="MatchStatsRpc"/>).</summary>
    public struct XpAwardRpc : IRpcCommand
    {
        public byte Reason;
        public int Amount;
        public int Other;
        public int DamageXp;
    }

    /// <summary>Damage one attacker did to one victim in the victim's current life.</summary>
    public struct DamageShare
    {
        public int Victim;
        public int Attacker;
        public float Amount;
        public double LastHit;
    }

    /// <summary>
    /// Turns gameplay events (<see cref="MatchLogEntry"/>) into XP awards: kills, assists, repairs and evaded missiles.
    /// Pure, so edit-mode tests can run it (XpAwardsTests). <see cref="MatchStatsSystem"/> calls it after the stats
    /// rules and keeps the damage shares between frames.
    /// </summary>
    public static class XpAwards
    {
        public static void Collect(in MatchLogEntry entry, NativeList<DamageShare> shares, in XpRates rates, NativeList<XpAward> awards)
        {
            var other = entry.Actor != 0 && entry.Actor != entry.Target;
            switch (entry.Kind)
            {
                case MatchLogKind.Hit:
                    if (other && entry.Target != 0 && entry.Amount > 0f) AddShare(shares, entry.Target, entry.Actor, entry.Amount, entry.Time);
                    break;

                case MatchLogKind.Kill:
                    OnKill(entry, other, shares, rates, awards);
                    break;

                case MatchLogKind.Repair:
                    if (entry.Actor == 0 || entry.Amount <= 0f) break;
                    var repairXp = (int)math.round(entry.Amount * rates.PerRepair);
                    if (repairXp > 0)
                        awards.Add(new XpAward { Player = entry.Actor, Reason = XpReason.Repair, Amount = repairXp });
                    break;

                case MatchLogKind.MissileDecoyed:
                    Evaded(entry, rates, awards);
                    break;

                case MatchLogKind.MissileEnd:
                    // Ran out of fuel still chasing its target: outflown. A decoyed missile chases the flare (target 0).
                    if (entry.Detail == (byte)MissileOutcome.Expired) Evaded(entry, rates, awards);
                    break;
            }
        }

        private static void Evaded(in MatchLogEntry entry, in XpRates rates, NativeList<XpAward> awards)
        {
            if (entry.Target == 0 || entry.Target == entry.Actor || rates.PerMissileEvaded <= 0) return;
            awards.Add(new XpAward { Player = entry.Target, Reason = XpReason.MissileEvaded, Amount = rates.PerMissileEvaded, Other = entry.Actor });
        }

        private static void OnKill(in MatchLogEntry entry, bool other, NativeList<DamageShare> shares, in XpRates rates, NativeList<XpAward> awards)
        {
            var victim = entry.Target;
            var botVictim = Bots.BotIds.IsBot(victim);
            var total = 0f;
            var killerDamage = 0f;
            for (var i = 0; i < shares.Length; i++)
            {
                if (shares[i].Victim != victim) continue;
                total += shares[i].Amount;
                if (shares[i].Attacker == entry.Actor) killerDamage += shares[i].Amount;
            }

            if (other && rates.PerKill > 0)
            {
                var amount = botVictim ? (int)math.round(rates.PerKill * math.saturate(rates.BotKillFactor)) : rates.PerKill;
                awards.Add(new XpAward
                {
                    Player = entry.Actor,
                    Reason = botVictim ? XpReason.BotKill : XpReason.Kill,
                    Amount = amount,
                    Other = victim,
                    DamageXp = (int)math.round(killerDamage * rates.PerDamage),
                });
            }

            // Assists: everyone else who did enough of this life's damage, recently enough.
            if (total > 0f && rates.PerAssist > 0)
            {
                var assistXp = botVictim ? (int)math.round(rates.PerAssist * math.saturate(rates.BotKillFactor)) : rates.PerAssist;
                for (var i = 0; i < shares.Length; i++)
                {
                    var share = shares[i];
                    if (share.Victim != victim || share.Attacker == entry.Actor || share.Attacker == victim) continue;
                    if (share.Amount / total < rates.AssistMinShare || entry.Time - share.LastHit > rates.AssistSeconds) continue;
                    if (assistXp > 0)
                        awards.Add(new XpAward { Player = share.Attacker, Reason = XpReason.Assist, Amount = assistXp, Other = victim });
                }
            }

            // The victim's next life starts clean.
            for (var i = shares.Length - 1; i >= 0; i--)
                if (shares[i].Victim == victim) shares.RemoveAtSwapBack(i);
        }

        private static void AddShare(NativeList<DamageShare> shares, int victim, int attacker, float amount, double time)
        {
            for (var i = 0; i < shares.Length; i++)
            {
                if (shares[i].Victim != victim || shares[i].Attacker != attacker) continue;
                var share = shares[i];
                share.Amount += amount;
                share.LastHit = time;
                shares[i] = share;
                return;
            }
            shares.Add(new DamageShare { Victim = victim, Attacker = attacker, Amount = amount, LastHit = time });
        }

        /// <summary>Adds an award to the player's stats, so the end screen counts the same XP the popups showed.</summary>
        public static void Count(in XpAward award, ref PlayerMatchStats stats)
        {
            switch (award.Reason)
            {
                case XpReason.Assist:
                    stats.Assists++;
                    stats.AssistXp += award.Amount;
                    break;
                case XpReason.Repair:
                    stats.RepairXp += award.Amount;
                    break;
                case XpReason.MissileEvaded:
                    stats.Evades++;
                    stats.EvadeXp += award.Amount;
                    break;
            }
        }
    }
}
