namespace GameLogic.StateMachine
{
    public abstract class GameState : IState
    {
        IStateMachine IState.StateMachine { get; set; }

        public virtual void OnEnter(IStateMachine stateMachine = null)
        {
        }

        public virtual void OnUpdate()
        {
        }
        
        public virtual void OnExit()
        {
        }

    }
}