using Events;

namespace GameLogic.StateMachine.MatchStateMachine
{
    public class InProgressGame : MatchState
    {
        public override void OnEnter(IStateMachine stateMachine = null)
        {
            base.OnEnter(stateMachine);
            if(!StateMachine.GetNetworkManager.IsServer) return;
            StateMachine.CurrentCountdownTimer = null;
            // Debug.Log("Game In Progress");
            GameEvents.OnInProgressGameEvent?.Invoke();
            GameEvents.OnPlayerUnFreezeAllEvent?.Invoke();
            GameEvents.OnEndMatchEvent += OnEndMatch;
        }

        private void OnEndMatch()
        {
            StateMachine.ChangeState(new PostGame());
        }

        public override void OnUpdate()
        {
            
        }

        public override void OnExit()
        {
            GameEvents.OnEndMatchEvent -= OnEndMatch;
        }

   
        
    }
}