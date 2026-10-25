using Unity.Netcode;

namespace PlayerInteraction.Networking
{
    public struct NetworkPlayerData : INetworkSerializable
    {
        public NetworkPlayerData( ulong playerNetworkID, int kills, int deaths)
        {
            PlayerNetworkID = playerNetworkID;
            Kills = kills;
            Deaths = deaths;
        }
        
        public ulong PlayerNetworkID;
        public int Kills;
        public int Deaths;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerNetworkID);
            serializer.SerializeValue(ref Kills);
            serializer.SerializeValue(ref Deaths);
        }
        
        public NetworkPlayerData AddKill()
        {
            Kills++;
            return this;
        }

        public NetworkPlayerData AddDeath()
        {
            Deaths++;
            return this;
        }
    }
}