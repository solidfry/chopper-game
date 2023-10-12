using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Networking
{
    public class ServerSpawnManager : SingletonNetwork<ServerSpawnManager>
    {
        private NetworkManager _networkManager;
        
        [SerializeField] List<SpawnLocation> teamASpawnLocations = new(6);
        [SerializeField] List<SpawnLocation> teamBSpawnLocations = new(6);
        

        public override void Awake()
        {
            base.Awake();
            
            _networkManager = NetworkManager.Singleton;
            if (_networkManager != null)
            {
                _networkManager.ConnectionApprovalCallback = ApprovalCheck;
                _networkManager.OnClientDisconnectCallback += OnClientDisconnectCallback;
            }
        }

        private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            var id= request.ClientNetworkId;
            var tr = UseSpawnLocation();
            

            if (tr != null)
            {
                response.Approved = true;
                response.CreatePlayerObject = true;
                response.Position = tr.position;
                response.Rotation = tr.rotation;
                Debug.Log(response.Position + " The position");
                response.Reason = "Testing the Approved approval message";
            }
            else
            {
                response.Approved = false;
                response.Reason = "No spawn location available";
            }

        }

        private void OnClientDisconnectCallback(ulong obj)
        {
            if (!_networkManager.IsServer && _networkManager.DisconnectReason != string.Empty)
            {
                Debug.Log($"Approval Declined Reason: {_networkManager.DisconnectReason}");
            }
        }


        public Transform UseSpawnLocation()
        {
            foreach (var spawnLocation in teamASpawnLocations)
            {
                if (!spawnLocation.IsPositionUsed())
                {
                    spawnLocation.UsePosition();
                    return spawnLocation.Transform;
                }
            }
            return null;
        }
    
       
    }
}
