using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Loadout
{
    /// <summary>
    /// A loadout in a few bytes, for RPCs and ghost fields: chassis id plus up to <see cref="MaxSlots"/> module ids,
    /// four per <c>ulong</c>. 0 = empty slot. Pure, Burst friendly.
    /// </summary>
    public struct LoadoutCode
    {
        public const int MaxSlots = 8;

        public ushort ChassisId;
        public ulong Modules0;
        public ulong Modules1;

        public bool IsEmpty => ChassisId == 0;

        public ushort GetModule(int slot)
        {
            if (slot < 0 || slot >= MaxSlots) return 0;
            var word = slot < 4 ? Modules0 : Modules1;
            return (ushort)(word >> ((slot % 4) * 16));
        }

        public void SetModule(int slot, ushort moduleId)
        {
            if (slot < 0 || slot >= MaxSlots) return;
            var shift = (slot % 4) * 16;
            var mask = ~(0xFFFFUL << shift);
            if (slot < 4) Modules0 = (Modules0 & mask) | ((ulong)moduleId << shift);
            else Modules1 = (Modules1 & mask) | ((ulong)moduleId << shift);
        }

        public bool Equals(in LoadoutCode other) =>
            ChassisId == other.ChassisId && Modules0 == other.Modules0 && Modules1 == other.Modules1;

        public static LoadoutCode From(in LoadoutSelection selection)
        {
            var code = new LoadoutCode { ChassisId = selection.ChassisId };
            for (var i = 0; i < selection.Modules.Count && i < MaxSlots; i++)
                code.SetModule(i, selection.Modules[i]);
            return code;
        }

        public LoadoutSelection ToSelection(int slotCount)
        {
            var modules = new ushort[System.Math.Min(slotCount, MaxSlots)];
            for (var i = 0; i < modules.Length; i++) modules[i] = GetModule(i);
            return new LoadoutSelection(ChassisId, modules);
        }
    }

    /// <summary>
    /// The chopper's loadout, decided by the server and sent to everyone. Every world applies it from its own catalog
    /// (<see cref="LoadoutApplySystem"/>), so the owner predicts with the same flight and weapons as the server.
    /// </summary>
    public struct ChopperLoadout : IComponentData
    {
        [GhostField] public ushort ChassisId;
        [GhostField] public ulong Modules0;
        [GhostField] public ulong Modules1;

        public LoadoutCode Code => new() { ChassisId = ChassisId, Modules0 = Modules0, Modules1 = Modules1 };

        public static ChopperLoadout From(in LoadoutCode code) =>
            new() { ChassisId = code.ChassisId, Modules0 = code.Modules0, Modules1 = code.Modules1 };
    }

    /// <summary>Local, not sent: the loadout this world last applied. Differs from <see cref="ChopperLoadout"/> = apply again.</summary>
    public struct AppliedLoadout : IComponentData
    {
        public LoadoutCode Code;
    }

    /// <summary>A socket on the chassis, relative to the chopper root. Baked from <see cref="SocketAuthoring"/>.</summary>
    [InternalBufferCapacity(8)]
    public struct ChassisSocket : IBufferElementData
    {
        public SlotType Slot;
        /// <summary>Which socket of this slot type (0 = first pylon, 1 = second pylon, ...).</summary>
        public byte Index;
        public Unity.Mathematics.float3 LocalPosition;
        public Unity.Mathematics.quaternion LocalRotation;
    }
}
