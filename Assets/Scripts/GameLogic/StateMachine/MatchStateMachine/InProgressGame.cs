using Events;
using UnityEngine;

namespace GameLogic.StateMachine.MatchStateMachine
{
    public class InProgressGame : MatchState
    {
        public override void OnEnter(IStateMachine stateMachine = null)
        {
            base.OnEnter(stateMachine);
            GameEvents.OnPlayerUnFreezeAllAllEvent?.Invoke();
        }
        
        public override void OnUpdate()
        {
        }

        public override void OnExit()
        {
        }

   
        
    }
}