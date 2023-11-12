using GameLogic.ScriptableObjects;
using Unity.Netcode;
using Utilities;

namespace GameLogic.StateMachine
{
    public interface IStateMachine
    {
        public void ChangeState(IState newState);
        public string GetCurrentStateName();
        public NetworkManager GetNetworkManager { get; }
        public CountdownTimer CurrentCountdownTimer { get; set; }
        public GameMode GameMode { get; }
    }
}