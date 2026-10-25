using System;
using Events;
using Unity.Netcode;
using UnityEngine;

namespace Interactions
{
    public class NetworkPlayerScore : NetworkBehaviour
    {
        public NetworkVariable<int> kills = new ();
        public NetworkVariable<int> deaths = new ();
        public NetworkVariable<int> score = new ();
        public static event Action<NetworkPlayerScore> OnPlayerSpawnedEvent;
        public static event Action<NetworkPlayerScore> OnPlayerDespawnedEvent;

        private bool _matchEnded;

        public override void OnNetworkSpawn()
        {
            OnPlayerSpawnedEvent?.Invoke(this);
            
            if (!IsServer) return;
            Debug.Log($"Score was initialised for {OwnerClientId}");
            kills.Value = 0;
            deaths.Value = 0;
            score.Value = 0;
            GameEvents.OnPlayerDiedEvent += AddDeath;
            GameEvents.OnPlayerKillEvent += AddKill;
            GameEvents.OnEndMatchEvent += MatchEnded;
        }
        
        private void MatchEnded()
        {
            _matchEnded = true;
        }
        
        public override void OnNetworkDespawn()
        {
            OnPlayerDespawnedEvent?.Invoke(this);
            
            if (!IsServer) return;
            GameEvents.OnPlayerDiedEvent -= AddDeath;
            GameEvents.OnPlayerKillEvent -= AddKill;
            GameEvents.OnEndMatchEvent -= MatchEnded;
        }
        

        void AddKill(ulong killerId)
        {
            if (!IsServer) return;
            if (killerId != OwnerClientId) return;
            if (_matchEnded) return;
            kills.Value += 1;
            score.Value += 100;
        }

        void AddDeath(ulong victimId)
        {
            if (!IsServer) return;
            if (victimId != OwnerClientId) return;
            if (_matchEnded) return;
            deaths.Value += 1;
            score.Value -= 50;
        }
    }
}