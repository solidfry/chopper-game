using Events;
using Networking;
using PlayerInteraction.Networking;
using UnityEngine;
using Utilities;

namespace GameLogic.StateMachine.MatchStateMachine
{
    public class StartGame : MatchState
    {
        private PlayerManager _playerPrefab;
        private ServerSpawnManager _serverSpawnManager;
        
        bool _timerStarted = false;
        float waitTime = 5f;

        CountdownTimer startGameTimer;

        public override void OnEnter(IStateMachine stateMachine = null)
        {
            base.OnEnter(stateMachine);
            if(!StateMachine.GetNetworkManager.IsServer) return;
            _serverSpawnManager = ServerSpawnManager.Instance;
            _playerPrefab = _serverSpawnManager.GetPlayerPrefab();
            // Instantiate the players and instantiate the timer and start it
            if (stateMachine != null && stateMachine.GetNetworkManager.IsServer)
            {
                foreach (var clientId in stateMachine.GetNetworkManager.ConnectedClients.Keys)
                {
                    // Assuming you have a method to create a player object
                    // GameObject playerObject ;
                    _serverSpawnManager.GetSpawnLocation(out var transform);
                    PlayerManager go = Object.Instantiate(_playerPrefab, transform.position, transform.rotation);
                    go.NetworkObject.SpawnAsPlayerObject(clientId);
                    // Assuming you have a method to get a starting position based on the client ID
                    
                }
            }
            GameEvents.OnPlayerFreezeAllAllEvent?.Invoke();
            GameEvents.OnSetTimerEvent?.Invoke(waitTime);
            startGameTimer = new CountdownTimer(waitTime, StateMachine.GetNetworkManager.ServerTime.FixedDeltaTime);
            GameEvents.OnTimerStartEvent?.Invoke();
            GameEvents.OnStartMatchEvent?.Invoke();
            startGameTimer.StartTimer();
            _timerStarted = true;
        }
        
        public override void OnUpdate()
        {
            if (_timerStarted && startGameTimer.CurrentTimeRemaining <= 0.5f)
            {
                _timerStarted = false;
                StateMachine.ChangeState(new InProgressGame());
            }
        }
        
        public override void OnFixedUpdate()
        {
            if (!_timerStarted) return;
            startGameTimer.OnUpdate();
        }
        
        public override void OnExit()
        {
            GameEvents.OnTimerEndEvent?.Invoke();
            startGameTimer = null;
        }

        
    }
}