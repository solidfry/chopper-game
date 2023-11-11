using System;
using UnityEngine;

namespace UI
{
    [Serializable]
    public class ClientPlayerScoreData 
    {
        [SerializeField] public ulong clientId;
        [SerializeField] public int kills;
        [SerializeField] public int deaths;

        public ClientPlayerScoreData( ulong clientId)
        {
            this.clientId = clientId;
            this.kills = 0;
            this.deaths = 0;
        }
        
        public void UpdatePlayerScore(int k, int d)
        {
            this.kills = k;
            this.deaths = d;
        }
    }
}