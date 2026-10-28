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
    }

    /// <summary>Server, on a connection entity: the loadout the player asked for; used at every (re)spawn.</summary>
    public struct RequestedLoadout : IComponentData
    {
        public LoadoutCode Code;
        public ushort SkinId;
    }
}
