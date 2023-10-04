namespace StateMachine
{
    public interface IStateMachine
    {
        public void ChangeState(IState newState);
        public string GetCurrentStateName();
    }
}