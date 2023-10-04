namespace StateMachine
{
    public interface IState
    {
        internal IStateMachine StateMachine { get; set; }
        public void OnEnter(IStateMachine stateMachine = null);
        public void OnUpdate(IStateMachine stateMachine = null);
        public void OnExit();
    }
}