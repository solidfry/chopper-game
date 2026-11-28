using Chopper.Simulation.Bots;
using Chopper.Simulation.Loadout;
using NUnit.Framework;
using Unity.Collections;

namespace Chopper.Tests
{
    /// <summary>Capture the Nuke bot class mix (owner, 28 Nov 2026: a balanced amount of chassis types per team).</summary>
    public sealed class BotClassMixTests
    {
        private static NativeArray<BotClassShare> Shares() => new(new[]
        {
            new BotClassShare { Class = ChassisClass.Gunship, Weight = 2 },
            new BotClassShare { Class = ChassisClass.Lifter, Weight = 1 },
            new BotClassShare { Class = ChassisClass.Assault, Weight = 1 },
            new BotClassShare { Class = ChassisClass.Scout, Weight = 1 },
        }, Allocator.Temp);

        private static ChassisClass PickWith(params ChassisClass[] onTeam)
        {
            var shares = Shares();
            var counts = new NativeArray<int>(8, Allocator.Temp);
            foreach (var c in onTeam) counts[(int)c]++;
            var pick = BotClassMix.Pick(shares, counts);
            shares.Dispose();
            counts.Dispose();
            return pick;
        }

        [Test]
        public void Pick_FillsTheTeamInWeightOrder()
        {
            Assert.That(PickWith(), Is.EqualTo(ChassisClass.Gunship), "empty team: the biggest share first");
            Assert.That(PickWith(ChassisClass.Gunship), Is.EqualTo(ChassisClass.Lifter), "then the first row still empty");
            Assert.That(PickWith(ChassisClass.Gunship, ChassisClass.Lifter), Is.EqualTo(ChassisClass.Assault));
            Assert.That(PickWith(ChassisClass.Gunship, ChassisClass.Lifter, ChassisClass.Assault), Is.EqualTo(ChassisClass.Scout));
            Assert.That(PickWith(ChassisClass.Gunship, ChassisClass.Lifter, ChassisClass.Assault, ChassisClass.Scout),
                Is.EqualTo(ChassisClass.Gunship), "a full set: the weight-2 class gets its second");
        }

        [Test]
        public void Pick_CountsPeopleToo()
        {
            Assert.That(PickWith(ChassisClass.Scout, ChassisClass.Scout), Is.EqualTo(ChassisClass.Gunship),
                "two people on scouts: the bot fills what is missing, not another scout");
        }

        [Test]
        public void PickOption_ChoosesOnlyFromTheWantedClass()
        {
            var options = new NativeArray<BotLoadoutOption>(new[]
            {
                new BotLoadoutOption { Class = ChassisClass.Scout, Code = new LoadoutCode { ChassisId = 1, Modules0 = 1 } },
                new BotLoadoutOption { Class = ChassisClass.Lifter, Code = new LoadoutCode { ChassisId = 4, Modules0 = 2 } },
                new BotLoadoutOption { Class = ChassisClass.Lifter, Code = new LoadoutCode { ChassisId = 4, Modules0 = 3 } },
            }, Allocator.Temp);
            Assert.That(BotClassMix.PickOption(options, ChassisClass.Lifter, 0f, out var first), Is.True);
            Assert.That(first.Modules0, Is.EqualTo(2ul));
            Assert.That(BotClassMix.PickOption(options, ChassisClass.Lifter, 0.99f, out var second), Is.True);
            Assert.That(second.Modules0, Is.EqualTo(3ul));
            Assert.That(BotClassMix.PickOption(options, ChassisClass.Gunship, 0.5f, out _), Is.False, "no build of that class");
            Assert.That(BotClassMix.ClassOf(options, 4), Is.EqualTo(ChassisClass.Lifter));
            Assert.That(BotClassMix.ClassOf(options, 9), Is.EqualTo(ChassisClass.None));
            options.Dispose();
        }
    }
}
