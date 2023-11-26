using Interactions;

namespace Interfaces
{
    public interface IPlayer
    {
        public NetworkHealth PlayerNetworkHealth { get; set; }
        public ulong PlayerNetworkID { get; set; }
    }
}