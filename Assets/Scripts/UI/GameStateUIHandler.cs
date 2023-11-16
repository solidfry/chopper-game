using Events;
using UI.ScriptableObjects;
using Unity.Netcode;
using UnityEngine;
using Utilities;

namespace UI
{
    public class GameStateUIHandler : SingletonNetwork<GameStateUIHandler>
    {
        [SerializeField] NetworkVariable<float> time;
        [SerializeField] TimerUI timerUI;
        [SerializeField] ColourData startMatchColourData;
        [SerializeField] CountdownTimer countdownTimer;
        
        public void Initialise()
        {
            if (!IsServer) return;
            GameEvents.OnSetTimerEvent += SetTimer;
            GameEvents.OnTimerStartEvent += StartTimer;
            GameEvents.OnTimerEndEvent += HideTimer;
            GameEvents.OnPostGameEvent += ShowPostGameUI;
            GameEvents.OnStartMatchEvent += UpdateStartMatchUI;
        }
        

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;
            GameEvents.OnSetTimerEvent -= SetTimer;
            GameEvents.OnTimerStartEvent -= StartTimer;
            GameEvents.OnTimerEndEvent -= HideTimer;
            GameEvents.OnPostGameEvent -= ShowPostGameUI;
            GameEvents.OnStartMatchEvent -= UpdateStartMatchUI;
        }

        private void SetTimer(float t)
        {
            if (!IsServer) return;
            time.Value = t;
            // Debug.Log("Timer set to " + time.Value + " seconds");
            countdownTimer = new CountdownTimer(time.Value, NetworkManager.Singleton.ServerTime.FixedDeltaTime);
            timerUI.SetTimer(time.Value);
        }
 
        private void FixedUpdate()
        {
            if (!IsServer) return;
            if (countdownTimer == null) return;
            
            countdownTimer.OnUpdate();
            time.Value = countdownTimer.CurrentTimeRemaining;
            timerUI.SetTimer(time.Value);
            SetTimer_ClientRpc(time.Value);
        }

        private void ShowPostGameUI()
        {
            if (!IsServer) return;
            OnEndMatch_ClientRpc();
        }

        private void StartTimer()
        {
            if (!IsServer) return;
            if (countdownTimer == null) return;
            timerUI.gameObject.SetActive(true);
            countdownTimer.StartTimer();
            timerUI.StartTimerUI();
            ShowTimer_ClientRpc();
        }
        
        private void HideTimer()
        {
            if (!IsServer) return;
            if (countdownTimer == null) return;
            timerUI.StopTimerUI();
            timerUI.gameObject.SetActive(false);
            HideTimer_ClientRpc();
        }
    
        private void UpdateStartMatchUI()
        {   
            if (!IsServer) return;
            // Debug.Log("Updating start match UI");
            timerUI.SetColors(startMatchColourData);
            UpdateStartMatchUI_ClientRpc();
        }

        [ClientRpc]
        private void UpdateStartMatchUI_ClientRpc()
        {
            GameEvents.OnNotificationEvent?.Invoke("Match Starting");
            timerUI.SetColors(startMatchColourData);
        }
    
        [ClientRpc]
        private void SetTimer_ClientRpc(float timeValue) => timerUI.SetTimer(timeValue);

        [ClientRpc]
        private void OnEndMatch_ClientRpc()
        {
            if(!IsClient) return;
            GameEvents.OnShowEndGameScreenEvent?.Invoke();
        }

        [ClientRpc] 
        private void ShowTimer_ClientRpc()
        {
            if(!IsClient) return;
            timerUI.gameObject.SetActive(true);
            timerUI.Show();
        }
    
        [ClientRpc]
        private void HideTimer_ClientRpc()
        {
            if(!IsClient) return;
            timerUI.Hide();
        }
        
    }
}
