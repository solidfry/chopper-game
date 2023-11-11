using System.Collections.Generic;
using Events;
using PlayerInteraction.Networking;
using Unity.Netcode;
using UnityEngine;

namespace Networking
{
    public class ScoreManager : SingletonNetwork<ScoreManager>
    {
        Dictionary<ulong, NetworkVariable<NetworkPlayerData>> _playerScores = new ();
        [SerializeField] int maxKills = 10; // This will come from a ScriptableObject
        [SerializeField] private NetworkVariable<int> totalKills = new (0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
        public override void Awake()
        {
            base.Awake();
            if (!IsServer) return;
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            GameEvents.OnPlayerDiedEvent += AddDeath;
            GameEvents.OnPlayerKillEvent += AddKill;
            GameEvents.OnInProgressGameEvent += SendPlayerScores;
        }
    
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (!IsServer) return;
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            GameEvents.OnPlayerDiedEvent -= AddDeath;
            GameEvents.OnPlayerKillEvent -= AddKill;
            GameEvents.OnInProgressGameEvent -= SendPlayerScores;
        }

        private void OnClientConnected(ulong clientId)
        {
            if(!IsServer) return;
            
                var player = new NetworkVariable<NetworkPlayerData>
                (
                    readPerm: NetworkVariableReadPermission.Everyone,
                    writePerm: NetworkVariableWritePermission.Server,
                    value: new NetworkPlayerData
                    {
                        PlayerNetworkID = clientId,
                        Kills = 0,
                        Deaths = 0
                    }
                );

                _playerScores.Add(clientId, player);
                Debug.Log("Player connected " + clientId + " and added to scoreboard player " + player.Value.PlayerNetworkID);
        }
        
    
        private void OnClientDisconnected(ulong clientId)
        {
            if(!IsServer) return;

            GameEvents.OnRemovePlayerScoreEvent?.Invoke(clientId);
            RemovePlayerScore_ClientRpc(clientId); 
            _playerScores.Remove(clientId);
        }
    
        void AddKill(ulong clientId)
        {
            if (!IsServer) return;
        
            _playerScores.TryGetValue(clientId, out var networkPlayerData);
        
            if (networkPlayerData != null)
            {
                UpdatePlayerData(clientId, 1);
                Debug.Log("Player kills updated for " + clientId + " and is now " + networkPlayerData.Value.Kills);
                totalKills.Value += 1;
            }

            CheckTotalKills();
        }

        void AddDeath(ulong clientId)
        {
            if (!IsServer) return;
  
            _playerScores.TryGetValue(clientId, out var networkPlayerData);
        
            if (networkPlayerData != null)
            {
                UpdatePlayerData(clientId, addDeaths:1);
                Debug.Log("Player deaths updated for " + clientId + " and is now " + networkPlayerData.Value.Deaths);
            }
        }

        private void UpdatePlayerData(ulong clientId, int addKills = 0, int addDeaths = 0)
        {
            if (!IsServer) return;
            
            var oldPlayerData = _playerScores[clientId];
            var kills = oldPlayerData.Value.Kills + addKills;
            var deaths = oldPlayerData.Value.Deaths + addDeaths;
            
            GameEvents.OnUpdatePlayerScoreEvent?.Invoke(clientId, kills, deaths);
            
            var playerData = new NetworkVariable<NetworkPlayerData> {
                Value = new NetworkPlayerData
                {
                    PlayerNetworkID = clientId,
                    Kills = kills,
                    Deaths = deaths
                }
            };
            
            _playerScores[clientId] = playerData;

            SendPlayerScore_ClientRpc(clientId, kills, deaths);
        }
    
        void CheckTotalKills()
        {
            if (!IsMatchOver) return;
            EndMatch();
        }
    
        public bool IsMatchOver => totalKills.Value >= maxKills;

        [ContextMenu("End Match")]
        private void EndMatch()
        {
            GameEvents.OnEndMatchEvent?.Invoke();
            GameEvents.OnPlayerFreezeAllEvent?.Invoke();
        }
    
        private void SendPlayerScores()
        {
            if (!IsServer) return;
            foreach (var player in _playerScores)
            {
                SendPlayerScore_ClientRpc(player.Key, player.Value.Value.Kills, player.Value.Value.Deaths);
                // Debug.Log("Sending player scores to clients");
            }
        }
    
        [ClientRpc] 
        private void SendPlayerScore_ClientRpc(ulong clientId, int kills, int deaths)
        {
            GameEvents.OnUpdatePlayerScoreEvent?.Invoke(clientId, kills, deaths);
            GameEvents.OnNotificationEvent?.Invoke($"You have {kills} kills and {deaths} deaths");
        }
    
        [ClientRpc]
        private void RemovePlayerScore_ClientRpc(ulong clientId)
        {
            if(clientId != NetworkManager.LocalClientId) return;
            GameEvents.OnRemovePlayerScoreEvent?.Invoke(clientId);
        }
    
        public override void OnDestroy()
        {
            base.OnDestroy();
            if(!IsServer) return;
        
            _playerScores.Clear();
        }
    }
}
