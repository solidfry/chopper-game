using Events;
using Unity.Netcode;

namespace GameLogic.StateMachine.MatchStateMachine
{
    public class PreGame : MatchState
    {
        
        public override void OnEnter(IStateMachine stateMachine = null)
        {
            base.OnEnter(stateMachine);
            // Set the timer to 60 seconds 
            // GameEvents.OnSetTimerEvent?.Invoke(timer.Value);
            // Start the timer
            GameEvents.OnTimerStartEvent?.Invoke();
        }

        public override void OnExit()
        {
            base.OnExit();
            GameEvents.OnTimerEndEvent?.Invoke();
        }

        public override void OnUpdate()
        {
            if (StateMachine == null) return;
            
            if(StateMachine.GetNetworkManager.ConnectedClients.Count >= 6)
                StateMachine.ChangeState(new StartGame());
        }
        
    }
}