using Events;
using Unity.Netcode;
using UnityEngine;
using Utilities;

namespace UI
{
    public class GameStateUIHandler : NetworkBehaviour
    {
        [SerializeField] NetworkVariable<float> time = new (60);
        [SerializeField] GameObject endGamePanel;
        [SerializeField] TimerUI timerUI;
    
        [SerializeField] private CountdownTimer countdownTimer = null;

        private void Awake()
        {
            if(timerUI == null) timerUI = GetComponentInChildren<TimerUI>();
        }

        private void InitialiseTimer(float t)
        {
            time.Value = t;
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
                GameEvents.OnStartMatchEvent += UpdateStartMatchUI;
            }

            if(IsClient && IsLocalPlayer)
            {
                endGamePanel.SetActive(false);
                timerUI.HideTimer();
            }
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

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if(!IsServer) return;
            GameEvents.OnSetTimerEvent -= InitialiseTimer;
            GameEvents.OnTimerStartEvent -= StartTimer;
            GameEvents.OnTimerStartEvent -= ShowTimerClientRpc;
            GameEvents.OnTimerEndEvent -= HideTimerClientRpc;
            GameEvents.OnEndMatchEvent -= OnEndMatchClientRpc;
            GameEvents.OnStartMatchEvent -= UpdateStartMatchUI;
        }
    
        private void StartTimer()
        {
            if (!IsServer) return;
            if (countdownTimer == null) return;
            countdownTimer.StartTimer();
            timerUI.StartTimer();
        }
    
        private void UpdateStartMatchUI()
        {   
            if (!IsServer) return;
            Debug.Log("Updating start match UI");
            timerUI.SetColors("Green", "DarkGreen");
            UpdateStartMatchUIClientRpc();
        }

        [ClientRpc]
        private void UpdateStartMatchUIClientRpc()
        {
            GameEvents.OnNotificationEvent?.Invoke("Match Starting");
            timerUI.SetColors("Green", "DarkGreen");
        }
    
        [ClientRpc]
        private void SetTimerClientRpc(float timeValue)
        {
            timerUI.SetTimer(timeValue);
        }

        [ClientRpc]
        private void OnEndMatchClientRpc()
        {
            endGamePanel.SetActive(true);
        }
    
        [ClientRpc] 
        private void ShowTimerClientRpc()
        {
            timerUI.ShowTimer();
        }
    
        [ClientRpc]
        private void HideTimerClientRpc()
        {
            timerUI.HideTimer();
        }
    }
}
