using Unity.Netcode;

namespace GameLogic.StateMachine
{
    public interface IStateMachine
    {
        public void ChangeState(IState newState);
        public string GetCurrentStateName();
        public NetworkManager GetNetworkManager { get; }
    }
}