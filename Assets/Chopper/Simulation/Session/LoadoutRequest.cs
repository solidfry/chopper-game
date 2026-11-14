using Chopper.Simulation.Loadout;
using Unity.Entities;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// Client world singleton: the loadout this player wants (set by the hangar). Missing or empty = the server's
    /// default chopper.
    /// </summary>
    public struct LoadoutChoice : IComponentData
    {
        public LoadoutCode Code;
        /// <summary>Paint skin (cosmetic, <see cref="ChopperSkin"/>); 0 = the kit's own look.</summary>
        public ushort SkinId;
        /// <summary>
        /// The player's XP for this choice: account, the chassis class, the pilot. Sent with the join request; the
        /// server uses it for the unlock check until Cloud Save gives it the real numbers (plan section 6a-1).
        /// </summary>
        public int AccountXp;
        public int ChassisXp;
        public int PilotXp;
    }

    /// <summary>Server, on a player entity (connection or bot): the loadout the player asked for; used at every (re)spawn.</summary>
    public struct RequestedLoadout : IComponentData
    {
        public LoadoutCode Code;
        public ushort SkinId;
    }
}
