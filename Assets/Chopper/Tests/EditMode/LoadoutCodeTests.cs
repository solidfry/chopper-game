using Chopper.Simulation.Loadout;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class LoadoutCodeTests
    {
        [Test]
        public void EverySlotKeepsItsOwnModule()
        {
            var code = new LoadoutCode { ChassisId = 3 };
            for (var slot = 0; slot < LoadoutCode.MaxSlots; slot++)
                code.SetModule(slot, (ushort)(0xF000 + slot));

            for (var slot = 0; slot < LoadoutCode.MaxSlots; slot++)
                Assert.AreEqual((ushort)(0xF000 + slot), code.GetModule(slot), $"slot {slot}");
        }

        [Test]
        public void SetModuleReplacesOnlyThatSlot()
        {
            var code = new LoadoutCode { ChassisId = 2 };
            code.SetModule(3, 111);
            code.SetModule(4, 112);
            code.SetModule(3, 0xFFFF);
            code.SetModule(3, 131);

            Assert.AreEqual(131, code.GetModule(3));
            Assert.AreEqual(112, code.GetModule(4));
            Assert.AreEqual(0, code.GetModule(2));
        }

        [Test]
        public void SlotsOutOfRangeAreIgnored()
        {
            var code = new LoadoutCode { ChassisId = 1 };
            code.SetModule(-1, 101);
            code.SetModule(LoadoutCode.MaxSlots, 101);

            Assert.AreEqual(0UL, code.Modules0);
            Assert.AreEqual(0UL, code.Modules1);
            Assert.AreEqual(0, code.GetModule(LoadoutCode.MaxSlots));
        }

        [Test]
        public void SelectionRoundTrips()
        {
            var selection = new LoadoutSelection(4, new ushort[] { 101, 111, 131, LoadoutSelection.EmptySlot, 141, 151, 161, 171 });
            var back = LoadoutCode.From(selection).ToSelection(selection.Modules.Count);

            Assert.AreEqual(selection.ChassisId, back.ChassisId);
            CollectionAssert.AreEqual(selection.Modules, back.Modules);
        }

        [Test]
        public void GhostComponentRoundTrips()
        {
            var code = new LoadoutCode { ChassisId = 2 };
            code.SetModule(0, 101);
            code.SetModule(6, 171);

            Assert.IsTrue(ChopperLoadout.From(code).Code.Equals(code));
        }

        [Test]
        public void DifferentModulesAreNotEqual()
        {
            var a = new LoadoutCode { ChassisId = 2 };
            var b = new LoadoutCode { ChassisId = 2 };
            a.SetModule(1, 111);
            b.SetModule(1, 112);

            Assert.IsFalse(a.Equals(b));
            b.SetModule(1, 111);
            Assert.IsTrue(a.Equals(b));
        }
    }
}
