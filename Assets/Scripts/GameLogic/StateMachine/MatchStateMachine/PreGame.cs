using Events;
using UnityEngine;
using Utilities;

namespace GameLogic.StateMachine.MatchStateMachine
{
    public class PreGame : MatchState
    {
        bool _timerStarted = false;
        float _waitTime = 10f;
        int _playerCount = 2;
        private bool playerCountReached;
        
        public override void OnEnter(IStateMachine stateMachine = null)
        {
            base.OnEnter(stateMachine);
            if(!StateMachine.GetNetworkManager.IsServer) return;
            StateMachine.CurrentCountdownTimer = new CountdownTimer(_waitTime, StateMachine.GetNetworkManager.ServerTime.FixedDeltaTime);
            GameEvents.OnSetTimerEvent?.Invoke(_waitTime);
        }

        public override void OnUpdate()
        {
            if (StateMachine == null) return;

            if (!StateMachine.GetNetworkManager.IsServer) return;
            
            playerCountReached = StateMachine.GetNetworkManager.ConnectedClients.Count >= _playerCount;
            if (!_timerStarted && playerCountReached)
            {
                Debug.Log("Starting pregame timer");
                StateMachine.CurrentCountdownTimer.StartTimer();
                GameEvents.OnTimerStartEvent?.Invoke();
                _timerStarted = true;
            }
            
            if (_timerStarted && StateMachine.CurrentCountdownTimer.CurrentTimeRemaining <= 0.1f)
            {
                _timerStarted = false;
                StateMachine.ChangeState(new StartGame());
            }
        }
        
        public override void OnFixedUpdate()
        {
            if (!StateMachine.GetNetworkManager.IsServer) return;

            if (!_timerStarted) return;
            // Debug.Log(countdownTimer.CurrentTimeRemaining);
            StateMachine.CurrentCountdownTimer.OnUpdate();
        }
        
        public override void OnExit()
        {
            if (!StateMachine.GetNetworkManager.IsServer) return;

            GameEvents.OnTimerEndEvent?.Invoke();
            StateMachine.CurrentCountdownTimer = null;
        }
    }
}