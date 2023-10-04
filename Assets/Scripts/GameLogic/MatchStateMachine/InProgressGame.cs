using StateMachine;

namespace GameLogic.MatchStateMachine
{
    public class InProgressGame : MatchState
    {
        public override void OnEnter(IStateMachine stateMachine = null)
        {
            StateMachine = stateMachine;
        }
        
        public override void OnUpdate(IStateMachine stateMachine = null)
        {
        }

        public override void OnExit()
        {
        }

   
        
    }
}