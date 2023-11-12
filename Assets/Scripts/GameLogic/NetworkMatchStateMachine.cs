using GameLogic.ScriptableObjects;
using GameLogic.StateMachine;
using GameLogic.StateMachine.MatchStateMachine;
using Unity.Netcode;
using UnityEngine;
using Utilities;

namespace GameLogic
{
    public class NetworkMatchStateMachine : NetworkBehaviour, IStateMachine
    {
       
        // PreGame
            // wait for players to load
        
        // StartGame
            // move them to their spawn locations and start a short count down
            
        // InProgressGame
            // players can move around and interact with the world and attack each other
            
        // PostGame
            // show the scoreboard and wait for players to press a button to return to the lobby
        private IState CurrentState { get; set; }
        [SerializeField][ReadOnly] string matchStateName;
        [SerializeField] GameMode gameMode;
        
        [field: SerializeField] public CountdownTimer CurrentCountdownTimer { get; set; }
        
        public NetworkManager GetNetworkManager
        {
            get;
            private set;
        }
        
        public GameMode GameMode => gameMode;
        
        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            
            GetNetworkManager = NetworkManager.Singleton;
            ChangeState(new InitialisingGame());
        }

        private void Update()
        {
            if (!IsServer) return;
            
            matchStateName = GetCurrentStateName();
            CurrentState.OnUpdate();
        }
        
        private void FixedUpdate()
        {
            if (!IsServer) return;
            
            CurrentState.OnFixedUpdate();
        }

        public void ChangeState(IState newState)
        {
            if (CurrentState != null)
            {
                CurrentState.OnExit();
            }

            CurrentState = newState;
            CurrentState.OnEnter(this);
        }

        public string GetCurrentStateName() => CurrentState.GetType().Name;
        
    }
}
