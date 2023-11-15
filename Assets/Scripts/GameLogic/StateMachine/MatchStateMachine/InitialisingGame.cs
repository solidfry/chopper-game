using Events;
using Networking;
using UI;
using Unity.Netcode;
using UnityEngine;
using Utilities;

namespace GameLogic.StateMachine.MatchStateMachine
{
    public class InitialisingGame : MatchState
    {
   
        GameStateUIHandler _gameStateUIHandler = GameStateUIHandler.Instance;
        ServerSpawnManager _serverSpawnManager = ServerSpawnManager.Instance;
        NetworkManager _networkManager = NetworkManager.Singleton;
        ScoreManager _scoreManager = ScoreManager.Instance;
        
        public override void OnEnter(IStateMachine stateMachine = null)
        {
            base.OnEnter(stateMachine);
            Debug.Log("Initialising Game");
            GameEvents.OnSendGameModeEvent?.Invoke(StateMachine.GameMode);
        }
        
        bool AllServicesInitialised =>
            _gameStateUIHandler != null && 
            _serverSpawnManager != null && 
            _networkManager != null && 
            _scoreManager != null;

        public override void OnUpdate()
        {
            if (StateMachine == null) return;

            if (!StateMachine.GetNetworkManager.IsServer) return;
            
            if (AllServicesInitialised)
            {
                _gameStateUIHandler.Initialise();
                StateMachine.ChangeState(new PreGame());
            }
        }
        
        public override void OnFixedUpdate()
        {

           
        }
        
        public override void OnExit()
        {
            
        }
    }
}