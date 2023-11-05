using Events;
using UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using Utilities;

public class GameStateUIHandler : NetworkBehaviour
{
    
    [SerializeField] NetworkVariable<float> time = new (60, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    [SerializeField] GameObject endGamePanel;
    [SerializeField] TimerUI timerUI;
    
    [SerializeField] private CountdownTimer countdownTimer;

    private void Awake()
    {
        if(timerUI == null) timerUI = GetComponentInChildren<TimerUI>();
        if(!IsServer) return;
        GameEvents.OnSetTimerEvent += timerUI.SetTimer;
        GameEvents.OnTimerStartEvent += timerUI.StartTimer;
        GameEvents.OnTimerStartEvent += ShowTimerClientRpc;
        GameEvents.OnEndMatchEvent += OnEndMatchClientRpc;
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        countdownTimer = new CountdownTimer(time.Value, NetworkManager.Singleton.ServerTime.FixedDeltaTime);

        if(IsClient && IsOwner)
        {
            endGamePanel.SetActive(false);
            timerUI.gameObject.SetActive(false);
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        countdownTimer.OnUpdate();
        time.Value = countdownTimer.CurrentTimeRemaining;
        timerUI.SetTimer(time.Value);
        
        // TODO: Client needs to have the timer synced up with the server
        
    }

    private void ShowTimer() => timerUI.gameObject.SetActive(true);
    private void HideTimer() => timerUI.gameObject.SetActive(false);

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if(!IsServer) return;
        GameEvents.OnSetTimerEvent -= timerUI.SetTimer;
        GameEvents.OnTimerStartEvent -= timerUI.StartTimer;
        GameEvents.OnTimerStartEvent -= ShowTimerClientRpc;
        GameEvents.OnEndMatchEvent -= OnEndMatchClientRpc;
    }

    [ClientRpc]
    private void OnEndMatchClientRpc()
    {
        endGamePanel.SetActive(true);
    }
    
    [ClientRpc] 
    private void ShowTimerClientRpc()
    {
        ShowTimer();
    }
    
    [ClientRpc]
    private void HideTimerClientRpc()
    {
        HideTimer();
    }
}
