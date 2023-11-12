using Events;
using PlayerInteraction.Networking;
using Unity.Netcode;
using UnityEngine;

namespace Networking
{
    public class ScoreManager : SingletonNetwork<ScoreManager>
    {
        [SerializeField] int maxKills = 10; // This will come from a ScriptableObject
        [SerializeField] private NetworkVariable<int> totalKills = new (0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer) return;

            GameEvents.OnPlayerKillEvent += AddKill;
        }
    
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (!IsServer) return;

            GameEvents.OnPlayerKillEvent -= AddKill;
        }
    
        void AddKill(ulong clientId)
        {
            if (!IsServer) return;

            totalKills.Value += 1;
            CheckTotalKills();
        }
        
        public bool IsMatchOver => totalKills.Value >= maxKills;
    
        void CheckTotalKills()
        {
            if (!IsMatchOver) return;
            EndMatch();
        }
    

        [ContextMenu("End Match")]
        private void EndMatch()
        {
            GameEvents.OnEndMatchEvent?.Invoke();
            GameEvents.OnPlayerFreezeAllEvent?.Invoke();
        }
        
    }
}
