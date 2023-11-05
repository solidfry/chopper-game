namespace GameLogic.StateMachine.MatchStateMachine
{
    public class PreGame : MatchState
    {
        public override void OnEnter(IStateMachine stateMachine = null)
        {
            base.OnEnter(stateMachine);

        }

        public override void OnExit()
        {
        }

        public override void OnUpdate()
        {
            if (StateMachine != null)
            {
                if(StateMachine.GetNetworkManager.ConnectedClients.Count >= 2)
                    StateMachine.ChangeState(new StartGame());
            }
        }
        
    }
}