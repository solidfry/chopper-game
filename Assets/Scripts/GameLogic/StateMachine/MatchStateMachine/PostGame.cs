﻿using Events;

namespace GameLogic.StateMachine.MatchStateMachine
{
    public class PostGame : MatchState
    {
        public override void OnEnter(IStateMachine stateMachine = null)
        {
            base.OnEnter(stateMachine);
            GameEvents.OnPostMatchEvent?.Invoke();
        }

        public override void OnExit()
        {
        }

        public override void OnUpdate()
        {
        }
        
    }
}