using System.Collections.Generic;
using Chopper.Simulation.Loadout;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Chopper.Simulation.Bots
{
    public enum BotKind : byte
    {
        /// <summary>Flies a chopper like a player (the only kind built).</summary>
        Air = 0,
        /// <summary>Hook for ground units (plan 7a). Nothing spawns or drives one yet.</summary>
        Ground = 1,
    }

    /// <summary>
    /// Server only: a bot that takes an empty player slot. The entity is a player without a connection: it has a
    /// <see cref="Session.PlayerId"/>, a loadout and a respawn timer, so spawn, score and the kill feed treat it like
    /// a player. Made and removed by <see cref="ServerBotFillSystem"/>; flown by <see cref="ServerBotSystem"/>.
    /// </summary>
    public struct ServerBot : IComponentData
    {
        /// <summary>0, 1, 2…: the roster row (wraps) and the id (<see cref="BotIds"/>).</summary>
        public int Slot;
        public BotKind Kind;
    }

    /// <summary>Server only, on a bot: how this bot flies and fights (its roster row's profile, or the map's).</summary>
    public struct BotSkill : IComponentData
    {
        public BotHandling Value;
    }

    /// <summary>On the spawner: how many bots the server keeps. Baked from <see cref="BotSettings"/>.</summary>
    public struct BotFillRules : IComponentData
    {
        /// <summary>Bots fill the match up to this many players (people + bots).</summary>
        public int TargetPlayers;
        public int MaxBots;
        /// <summary>Seconds between two changes (one bot joins or leaves at a time).</summary>
        public float StepSeconds;
    }

    /// <summary>On the spawner: one roster row. Baked from <see cref="BotSettings"/>.</summary>
    [InternalBufferCapacity(0)]
    public struct BotRosterEntry : IBufferElementData
    {
        public FixedString32Bytes Name;
        public LoadoutCode Loadout;
        /// <summary>False = the map's own profile (the <see cref="BotHandling"/> singleton).</summary>
        public bool HasHandling;
        public BotHandling Handling;
    }

    /// <summary>
    /// Player ids of server bots. Netcode gives connections the ids 1, 2, 3… (and uses a freed id again), so bots start
    /// far above them. The same id is the chopper's <c>GhostOwner.NetworkId</c>: no client has it, so every client sees
    /// a bot's chopper as another player's.
    /// </summary>
    public static class BotIds
    {
        public const int First = 1001;

        public static int Id(int slot) => First + math.max(slot, 0);
        public static bool IsBot(int playerId) => playerId >= First;
        /// <summary>The slot of a bot id (0 for an id that is not a bot's).</summary>
        public static int Slot(int playerId) => math.max(playerId - First, 0);
    }

    /// <summary>One bot, for <see cref="BotFill.PickToRemove"/>.</summary>
    public struct BotStanding
    {
        public int Slot;
        public int Kills;
        /// <summary>It has a chopper in the air now.</summary>
        public bool Alive;
    }

    /// <summary>How many bots a match needs and which one goes when a person joins. Pure.</summary>
    public static class BotFill
    {
        /// <summary>
        /// Bots wanted: the gap between <paramref name="targetPlayers"/> and the people in the match, at most
        /// <paramref name="maxBots"/>. No people = no bots (an empty server stays idle and can stop).
        /// </summary>
        public static int Wanted(int humans, int targetPlayers, int maxBots)
        {
            if (humans <= 0) return 0;
            return math.clamp(targetPlayers - humans, 0, math.max(maxBots, 0));
        }

        /// <summary>The lowest slot that no bot has.</summary>
        public static int FreeSlot(in NativeArray<BotStanding> bots)
        {
            for (var slot = 0; ; slot++)
            {
                var used = false;
                for (var i = 0; i < bots.Length && !used; i++) used = bots[i].Slot == slot;
                if (!used) return slot;
            }
        }

        /// <summary>
        /// The index of the bot to remove (-1 = none): one with no chopper in the air first (nobody sees it vanish),
        /// then the fewest kills (the leaders stay), then the highest slot.
        /// </summary>
        public static int PickToRemove(in NativeArray<BotStanding> bots)
        {
            var best = -1;
            for (var i = 0; i < bots.Length; i++)
            {
                if (best < 0 || Before(bots[i], bots[best])) best = i;
            }
            return best;
        }

        private static bool Before(in BotStanding a, in BotStanding b)
        {
            if (a.Alive != b.Alive) return !a.Alive;
            if (a.Kills != b.Kills) return a.Kills < b.Kills;
            return a.Slot > b.Slot;
        }
    }

    /// <summary>The name players see for a bot. Pure; the server log and the client HUD use the same rule.</summary>
    public static class BotNames
    {
        public const string Fallback = "BOT";

        /// <summary>
        /// "[BOT] RAZOR" for slot 0 with the tag "[BOT]". More bots than names: the names start again with a number
        /// ("[BOT] RAZOR 2"). No names: "[BOT] BOT 1".
        /// </summary>
        public static string For(IReadOnlyList<string> names, string tag, int slot)
        {
            slot = slot < 0 ? 0 : slot;
            var count = names != null ? names.Count : 0;
            string name;
            if (count == 0)
            {
                name = $"{Fallback} {slot + 1}";
            }
            else
            {
                var row = names[slot % count];
                name = string.IsNullOrWhiteSpace(row) ? Fallback : row.Trim();
                var round = slot / count;
                if (round > 0) name = $"{name} {round + 1}";
            }
            return string.IsNullOrWhiteSpace(tag) ? name : $"{tag.Trim()} {name}";
        }
    }
}
