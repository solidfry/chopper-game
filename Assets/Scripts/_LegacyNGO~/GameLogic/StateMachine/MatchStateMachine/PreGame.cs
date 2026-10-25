using Events;
using GameLogic.ScriptableObjects;
using UnityEngine;
using Utilities;

namespace GameLogic.StateMachine.MatchStateMachine
{
    public class PreGame : MatchState
    {
        bool _timerStarted = false;
        float _waitTime = 10f;

        private GameMode gameMode;
        
        public override void OnEnter(IStateMachine stateMachine = null)
        {
            base.OnEnter(stateMachine);
            if(!StateMachine.GetNetworkManager.IsServer) return;
            StateMachine.CurrentCountdownTimer = new CountdownTimer(HandleWaitTime(), StateMachine.GetNetworkManager.ServerTime.FixedDeltaTime);
            GameEvents.OnSetTimerEvent?.Invoke(HandleWaitTime());
            gameMode = StateMachine.GameMode != null ? StateMachine.GameMode : null;
        }

        private float HandleWaitTime() => _waitTime = StateMachine.GameMode != null ? StateMachine.GameMode.PreGameCountdownTime : _waitTime;

        bool PlayerCountReached()
        {
            if(StateMachine.GameMode != null) 
                return StateMachine.GetNetworkManager.ConnectedClients.Count >= StateMachine.GameMode.GameStartPlayerCount;
            return StateMachine.GetNetworkManager.ConnectedClients.Count >= 2;
        }
        
        public override void OnUpdate()
        {
            if (StateMachine == null) return;

            if (!StateMachine.GetNetworkManager.IsServer) return;
            
     
            if (!_timerStarted && PlayerCountReached())
            {
                // Debug.Log("Starting pregame timer");
                StateMachine.CurrentCountdownTimer.StartTimer();
                GameEvents.OnPreMatchEvent?.Invoke();
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