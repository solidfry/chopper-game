using Events;
using Utilities;

namespace GameLogic.StateMachine.MatchStateMachine
{
    public class PreGame : MatchState
    {
        bool _timerStarted = false;
        float waitTime = 10f;
        
        
        CountdownTimer countdownTimer;
        public override void OnEnter(IStateMachine stateMachine = null)
        {
            base.OnEnter(stateMachine);
            if(!StateMachine.GetNetworkManager.IsServer) return;
            // TODO: get this from an SO
            GameEvents.OnSetTimerEvent?.Invoke(waitTime);
            // Debug.Log(waitTime + " seconds until game starts");
            countdownTimer = new CountdownTimer(waitTime, StateMachine.GetNetworkManager.ServerTime.FixedDeltaTime);
        }

        public override void OnUpdate()
        {
            if (StateMachine == null) return;

            if (!StateMachine.GetNetworkManager.IsServer) return;
            
            if (StateMachine.GetNetworkManager.ConnectedClients.Count >= 2 && !_timerStarted)
            {
                GameEvents.OnTimerStartEvent?.Invoke();
                // Debug.Log("Timer started from pregame state");
                countdownTimer.StartTimer();
                _timerStarted = true;
            }
            
            if (_timerStarted && countdownTimer.CurrentTimeRemaining <= 0.5f)
            {
                _timerStarted = false;
                StateMachine.ChangeState(new StartGame());
            }
        }
        
        public override void OnFixedUpdate()
        {
            if (!_timerStarted) return;
            // Debug.Log(countdownTimer.CurrentTimeRemaining);
            countdownTimer.OnUpdate();
        }
        
        public override void OnExit()
        {
            GameEvents.OnTimerEndEvent?.Invoke();
            countdownTimer = null;
        }
    }
}