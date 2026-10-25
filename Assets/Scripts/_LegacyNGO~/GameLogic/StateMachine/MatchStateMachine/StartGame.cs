using Events;
using GameLogic.ScriptableObjects;
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
        float _waitTime = 10f;
        
        private GameMode gameMode;


        public override void OnEnter(IStateMachine stateMachine = null)
        {
            base.OnEnter(stateMachine);
            if(!StateMachine.GetNetworkManager.IsServer) return;
            _serverSpawnManager = ServerSpawnManager.Instance;
            _playerPrefab = _serverSpawnManager.GetPlayerPrefab();
            gameMode = StateMachine.GameMode != null ? StateMachine.GameMode : null;
            // Instantiate the players and instantiate the timer and start it
            if (stateMachine != null && stateMachine.GetNetworkManager.IsServer)
            {
                if(_serverSpawnManager is not null && _serverSpawnManager.GetSpawnLocationsCount != 0)
                {
                    AllocateSpawns(stateMachine);
                }
            }
            
            HandleStartTimer();
        }
        
        private float HandleWaitTime() => _waitTime = StateMachine.GameMode != null ? StateMachine.GameMode.GameStartCountdownTime : _waitTime;

        private void HandleStartTimer()
        {
            if(!StateMachine.GetNetworkManager.IsServer) return;
            _waitTime = HandleWaitTime();
            StateMachine.CurrentCountdownTimer = new CountdownTimer(_waitTime, StateMachine.GetNetworkManager.ServerTime.FixedDeltaTime);
            GameEvents.OnPlayerFreezeAllEvent?.Invoke();
            GameEvents.OnSetTimerEvent?.Invoke(_waitTime);
            GameEvents.OnStartMatchEvent?.Invoke();
            StateMachine.CurrentCountdownTimer.StartTimer();
            GameEvents.OnTimerStartEvent?.Invoke();
            _timerStarted = true;
        }
        
        public override void OnUpdate()
        {
            if(!StateMachine.GetNetworkManager.IsServer) return;

            if (_timerStarted && StateMachine.CurrentCountdownTimer.CurrentTimeRemaining <= 0.5f)
            {
                _timerStarted = false;
                StateMachine.ChangeState(new InProgressGame());
            }
        }
        
        public override void OnFixedUpdate()
        {
            if(!StateMachine.GetNetworkManager.IsServer) return;

            if (!_timerStarted) return;
            StateMachine.CurrentCountdownTimer.OnUpdate();
        }
        
        public override void OnExit()
        {
            if(!StateMachine.GetNetworkManager.IsServer) return;

            GameEvents.OnTimerEndEvent?.Invoke();
            GameEvents.OnPlayerUnFreezeAllEvent?.Invoke();
            StateMachine.CurrentCountdownTimer = null;
        }
        
        private void AllocateSpawns(IStateMachine stateMachine)
        {
            foreach (var clientId in stateMachine.GetNetworkManager.ConnectedClients.Keys)
            {
                SpawnPlayerObject(clientId);
            }
        }

        private void SpawnPlayerObject(ulong clientId)
        {
            _serverSpawnManager.GetSpawnLocation(out var transform);
            PlayerManager go = Object.Instantiate(_playerPrefab, transform.position, transform.rotation);
            go.NetworkObject.SpawnAsPlayerObject(clientId);
        }

        
    }
}