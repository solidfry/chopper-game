using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Loadout
{
    /// <summary>
    /// Cosmetic: the chopper's paint skin (a <c>SkinLibrary</c> id; 0 or unknown = the kit's own look). The server copies
    /// it from the player's request at every spawn and sends it to everyone; clients only show it (no gameplay effect).
    /// TODO (progression): the server checks the player owns the skin before it copies it.
    /// </summary>
    public struct ChopperSkin : IComponentData
    {
        [GhostField] public ushort SkinId;
    }
}
