using System.Collections;
using System.Collections.Generic;
using Events;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

class PlayerScore
{
    public int Kills = 0;
    public int Deaths = 0;
}

public class ScoreManager : NetworkBehaviour
{
    Dictionary<NetworkClient, PlayerScore> playerScores = new ();
    [SerializeField] int maxKills = 10; // This will come from a ScriptableObject
    [SerializeField] private int totalKills = 0;
    [SerializeField] Canvas scoreboardCanvasPrefab;
    [SerializeField] InputAction showScoreboardAction;
    
    public int TotalKills
    {
        get => totalKills;
        private set
        {
            totalKills = value; 
            if(!IsServer) return;
            if (totalKills >= maxKills) EndConditionsMet();
        }
    }
    
    private void Awake()
    {
        if (!IsServer) return;
        NetworkManager.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        GameEvents.OnPlayerKillEvent += AddKill;
        GameEvents.OnPlayerDiedEvent += AddDeath;
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        
        if (!IsServer) return;

        NetworkManager.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        GameEvents.OnPlayerKillEvent -= AddKill;
        GameEvents.OnPlayerDiedEvent -= AddDeath;
    }

    private void OnClientConnected(ulong client)
    {
        playerScores.Add(NetworkManager.Singleton.ConnectedClients[client], new PlayerScore());
    }
    
    private void OnClientDisconnected(ulong client)
    {
        playerScores.Remove(NetworkManager.Singleton.ConnectedClients[client]);
    }
    
    void AddKill(ulong client)
    {
        if (!IsServer) return;
        
        NetworkManager.Singleton.ConnectedClients.TryGetValue(client, out var networkClient);
        if(networkClient == null) return;
        
        playerScores[networkClient].Kills++;
        totalKills++;
    }
    
    void AddDeath(ulong client)
    {
        if (!IsServer) return;
        
        NetworkManager.Singleton.ConnectedClients.TryGetValue(client, out var networkClient);
        if(networkClient == null) return;
        
        playerScores[networkClient].Deaths++;
        print($"Player {client} died and has {playerScores[networkClient].Deaths} deaths");
    }
    
    [ContextMenu("End Match")]
    private void EndConditionsMet() => EndMatchClientRpc();
    
    [ClientRpc]
    private void EndMatchClientRpc()
    {
        GameEvents.OnPlayerFreezeAllAllEvent?.Invoke();
        GameEvents.OnEndMatchEvent?.Invoke();
    }

}
