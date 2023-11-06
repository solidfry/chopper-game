namespace GameLogic.StateMachine
{
    public abstract class MatchState : IState
    {

        public IStateMachine StateMachine { get; set; }

        public virtual void OnEnter(IStateMachine stateMachine = null) => StateMachine = stateMachine;

        public virtual void OnUpdate() { }
        public virtual void OnFixedUpdate() { }

        public virtual void OnExit() { }
    }
}

