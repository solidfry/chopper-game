namespace GameLogic.StateMachine
{
    public interface IState
    {
        public IStateMachine StateMachine { get; set; }
        public void OnEnter(IStateMachine stateMachine = null);
        public void OnUpdate();
        public void OnExit();
    }
}