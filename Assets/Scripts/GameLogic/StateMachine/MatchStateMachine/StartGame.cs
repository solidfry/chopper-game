using Events;
using Networking;
using PlayerInteraction.Networking;
using Unity.Netcode;
using UnityEngine;

namespace GameLogic.StateMachine.MatchStateMachine
{
    public class StartGame : MatchState
    {
        private PlayerManager _playerPrefab;
        private ServerSpawnManager _serverSpawnManager;
        public override void OnEnter(IStateMachine stateMachine = null)
        {
            base.OnEnter(stateMachine);
            Debug.Log($"Game started");

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
            // GameEvents.OnPlayerFreezeAllAllEvent?.Invoke();
            GameEvents.OnStartMatchEvent?.Invoke();
        }

        public override void OnExit()
        {
        }

        public override void OnUpdate()
        {
            
        }
        
    }
}