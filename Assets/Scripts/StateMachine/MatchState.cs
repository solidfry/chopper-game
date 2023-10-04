namespace StateMachine
{
    public abstract class MatchState : IState
    {
        internal IStateMachine StateMachine;

        IStateMachine IState.StateMachine { get; set; }

        public virtual void OnEnter(IStateMachine stateMachine = null)
        {
            StateMachine = stateMachine;
        }

        public virtual void OnUpdate(IStateMachine stateMachine = null)
        {
        }
        
        public virtual void OnExit()
        {
        }
    }
}

