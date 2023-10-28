using Interactions;

namespace Interfaces
{
    public interface IPlayer
    {
        public NetworkHealth Health { get; set; }
        public ulong PlayerNetworkID { get; set; }
    }
}