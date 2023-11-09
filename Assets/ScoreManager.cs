using System.Collections.Generic;
using DG.Tweening;
using Events;
using PlayerInteraction.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class ScoreManager : NetworkBehaviour
{
    Dictionary<ulong, NetworkVariable<NetworkPlayerData>> _playerScores = new ();
    [SerializeField] int maxKills = 10; // This will come from a ScriptableObject
    [SerializeField] private NetworkVariable<int> totalKills = new (0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        NetworkManager.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        GameEvents.OnPlayerDiedEvent += AddDeath;
        GameEvents.OnPlayerKillEvent += AddKill;
    }

 
    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;
        NetworkManager.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        GameEvents.OnPlayerDiedEvent -= AddDeath;
        GameEvents.OnPlayerKillEvent -= AddKill;
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

        _playerScores.Remove(clientId);
    }
    
    void AddKill(ulong clientId)
    {
        if (!IsServer) return;
        
        _playerScores.TryGetValue(clientId, out var networkPlayerData);
        
        if (networkPlayerData != null)
        {
            UpdatePlayerData(clientId, 1);
            // var playerData = networkPlayerData.Value;
            // playerData.AddKill();
            // networkPlayerData.Value = playerData;
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

            // var playerData = networkPlayerData.Value;
            // playerData.AddDeath();
            // networkPlayerData.Value = playerData;
            Debug.Log("Player deaths updated for " + clientId + " and is now " + networkPlayerData.Value.Deaths);
        }
    }

    private void UpdatePlayerData(ulong client, int addKills = 0, int addDeaths = 0)
    {
        var oldPlayerData = _playerScores[client];
        var kills = oldPlayerData.Value.Kills + addKills;
        var deaths = oldPlayerData.Value.Deaths + addDeaths;
        var playerData = new NetworkPlayerData(client, kills, deaths);
        _playerScores[client] = new(playerData);
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
    
    public override void OnDestroy()
    {
        base.OnDestroy();
        if(!IsServer) return;
        
        _playerScores.Clear();
    }
    
    
}
