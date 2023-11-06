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
    
    [SerializeField] private CountdownTimer countdownTimer = null;

    private void Awake()
    {
        if(timerUI == null) timerUI = GetComponentInChildren<TimerUI>();
    }

    private void InitialiseTimer(float f)
    {
        time.Value = f;
        countdownTimer = new CountdownTimer(time.Value, NetworkManager.Singleton.ServerTime.FixedDeltaTime);
        timerUI.SetTimer(time.Value);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            GameEvents.OnSetTimerEvent += InitialiseTimer;
            GameEvents.OnTimerStartEvent += StartTimer;
            GameEvents.OnTimerStartEvent += ShowTimerClientRpc;
            GameEvents.OnTimerEndEvent += HideTimerClientRpc;
            GameEvents.OnEndMatchEvent += OnEndMatchClientRpc;
        }

        if(IsClient && IsLocalPlayer)
        {
            endGamePanel.SetActive(false);
            timerUI.gameObject.SetActive(false);
        }
    }

    private void StartTimer()
    {
        if (!IsServer) return;
        if (countdownTimer == null) return;
        countdownTimer.StartTimer();
        timerUI.StartTimer();
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (countdownTimer == null) return;
            
        countdownTimer.OnUpdate();
        time.Value = countdownTimer.CurrentTimeRemaining;
        timerUI.SetTimer(time.Value);
        SetTimerClientRpc(time.Value);

    }

    [ClientRpc]
    private void SetTimerClientRpc(float timeValue)
    {
        timerUI.SetTimer(timeValue);
    }

    private void ShowTimer() => timerUI.gameObject.SetActive(true);
    private void HideTimer() => timerUI.gameObject.SetActive(false);

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if(!IsServer) return;
        GameEvents.OnSetTimerEvent -= InitialiseTimer;
        GameEvents.OnTimerStartEvent -= StartTimer;
        GameEvents.OnTimerStartEvent -= ShowTimerClientRpc;
        GameEvents.OnTimerEndEvent -= HideTimerClientRpc;
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
