using Interactions;

namespace Interfaces
{
    public interface IPlayer
    {
        public NetworkHealth PlayerNetworkHealth { get; set; }
        public ulong PlayerOwnerNetworkId { get; set; }
    }
}