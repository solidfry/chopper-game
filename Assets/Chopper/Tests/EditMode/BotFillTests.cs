using Chopper.Simulation.Bots;
using NUnit.Framework;
using Unity.Collections;

namespace Chopper.Tests
{
    /// <summary>Server bots: how many (<see cref="BotFill"/>), their ids (<see cref="BotIds"/>) and names (<see cref="BotNames"/>).</summary>
    public sealed class BotFillTests
    {
        private static BotStanding Bot(int slot, int kills = 0, bool alive = true) => new() { Slot = slot, Kills = kills, Alive = alive };

        private static int Remove(params BotStanding[] bots)
        {
            using var array = new NativeArray<BotStanding>(bots, Allocator.Temp);
            return BotFill.PickToRemove(array);
        }

        private static int Free(params BotStanding[] bots)
        {
            using var array = new NativeArray<BotStanding>(bots, Allocator.Temp);
            return BotFill.FreeSlot(array);
        }

        [Test]
        public void Wanted_NoPeople_NoBots()
        {
            Assert.That(BotFill.Wanted(0, 4, 4), Is.EqualTo(0));
        }

        [Test]
        public void Wanted_FillsUpToTheTarget()
        {
            Assert.That(BotFill.Wanted(1, 4, 4), Is.EqualTo(3));
            Assert.That(BotFill.Wanted(3, 4, 4), Is.EqualTo(1));
        }

        [Test]
        public void Wanted_FullOrOverTarget_NoBots()
        {
            Assert.That(BotFill.Wanted(4, 4, 4), Is.EqualTo(0));
            Assert.That(BotFill.Wanted(7, 4, 4), Is.EqualTo(0));
        }

        [Test]
        public void Wanted_NeverMoreThanTheMaximum()
        {
            Assert.That(BotFill.Wanted(1, 12, 4), Is.EqualTo(4));
            Assert.That(BotFill.Wanted(1, 12, 0), Is.EqualTo(0));
            Assert.That(BotFill.Wanted(1, 12, -3), Is.EqualTo(0));
        }

        [Test]
        public void Wanted_APersonJoins_OneBotLess()
        {
            Assert.That(BotFill.Wanted(2, 4, 4), Is.EqualTo(BotFill.Wanted(1, 4, 4) - 1));
        }

        [Test]
        public void FreeSlot_IsTheLowestNotUsed()
        {
            Assert.That(Free(), Is.EqualTo(0));
            Assert.That(Free(Bot(0), Bot(1)), Is.EqualTo(2));
            Assert.That(Free(Bot(0), Bot(2)), Is.EqualTo(1));
            Assert.That(Free(Bot(2), Bot(1)), Is.EqualTo(0));
        }

        [Test]
        public void Remove_None_WhenThereAreNoBots()
        {
            Assert.That(Remove(), Is.EqualTo(-1));
        }

        [Test]
        public void Remove_ADeadBotFirst()
        {
            Assert.That(Remove(Bot(0, kills: 0), Bot(1, kills: 9, alive: false), Bot(2, kills: 1)), Is.EqualTo(1));
        }

        [Test]
        public void Remove_ThenTheFewestKills()
        {
            Assert.That(Remove(Bot(0, kills: 5), Bot(1, kills: 2), Bot(2, kills: 7)), Is.EqualTo(1));
        }

        [Test]
        public void Remove_ThenTheHighestSlot()
        {
            Assert.That(Remove(Bot(0, kills: 2), Bot(3, kills: 2), Bot(1, kills: 2)), Is.EqualTo(1));
        }

        [Test]
        public void Ids_StartAbovePlayerIds_AndGoBackToSlots()
        {
            Assert.That(BotIds.Id(0), Is.EqualTo(BotIds.First));
            Assert.That(BotIds.Id(3), Is.EqualTo(BotIds.First + 3));
            Assert.That(BotIds.Slot(BotIds.Id(5)), Is.EqualTo(5));
            Assert.That(BotIds.IsBot(BotIds.Id(0)), Is.True);
            Assert.That(BotIds.IsBot(1), Is.False);
            Assert.That(BotIds.IsBot(64), Is.False);
            Assert.That(BotIds.Slot(3), Is.EqualTo(0), "not a bot id");
        }

        [Test]
        public void Names_TagAndRosterName()
        {
            var names = new[] { "RAZOR", "GLITCH" };
            Assert.That(BotNames.For(names, "[BOT]", 0), Is.EqualTo("[BOT] RAZOR"));
            Assert.That(BotNames.For(names, "[BOT]", 1), Is.EqualTo("[BOT] GLITCH"));
        }

        [Test]
        public void Names_MoreBotsThanNames_StartAgainWithANumber()
        {
            var names = new[] { "RAZOR", "GLITCH" };
            Assert.That(BotNames.For(names, "[BOT]", 2), Is.EqualTo("[BOT] RAZOR 2"));
            Assert.That(BotNames.For(names, "[BOT]", 5), Is.EqualTo("[BOT] GLITCH 3"));
        }

        [Test]
        public void Names_NoTag_NoNames_EmptyName()
        {
            Assert.That(BotNames.For(new[] { "RAZOR" }, "", 0), Is.EqualTo("RAZOR"));
            Assert.That(BotNames.For(null, "[BOT]", 0), Is.EqualTo("[BOT] BOT 1"));
            Assert.That(BotNames.For(new string[0], null, 2), Is.EqualTo("BOT 3"));
            Assert.That(BotNames.For(new[] { " " }, "[BOT]", 0), Is.EqualTo("[BOT] BOT"));
        }

        [Test]
        public void Names_AreDifferentForEachSlot()
        {
            var names = new[] { "RAZOR", "GLITCH", "NEON" };
            var seen = new System.Collections.Generic.HashSet<string>();
            for (var slot = 0; slot < 8; slot++)
                Assert.That(seen.Add(BotNames.For(names, "[BOT]", slot)), Is.True, $"slot {slot}");
        }
    }
}
