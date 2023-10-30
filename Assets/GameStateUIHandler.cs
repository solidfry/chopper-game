using Events;
using Unity.Netcode;
using UnityEngine;

public class GameStateUIHandler : NetworkBehaviour
{
    [SerializeField] GameObject endGamePanel;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        endGamePanel.SetActive(false);
        if(!IsServer) return;
        GameEvents.OnEndMatchEvent += OnEndMatchClientRpc;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if(!IsServer) return;
        GameEvents.OnEndMatchEvent -= OnEndMatchClientRpc;
    }

    [ClientRpc]
    private void OnEndMatchClientRpc()
    {
        endGamePanel.SetActive(true);
    }
}
