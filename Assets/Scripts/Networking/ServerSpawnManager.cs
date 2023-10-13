using System;
using System.Collections.Generic;
using Networking.Spawns;
using Unity.Netcode;
using UnityEngine;

namespace Networking
{
    public class ServerSpawnManager : SingletonNetwork<ServerSpawnManager>
    {
        // private NetworkManager _networkManager;
        [SerializeField] List<TeamSpawnLocations> teamSpawnLocations = new();


        // private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        // {
        //     var id= request.ClientNetworkId;
        //     var tr = UseSpawnLocation();


        //     if (tr != null)
        //     {
        //         response.Approved = true;
        //         response.CreatePlayerObject = true;
        //         response.Position = tr.position;
        //         response.Rotation = tr.rotation;
        //         Debug.Log(response.Position + " The position");
        //         response.Reason = "Testing the Approved approval message";
        //     }
        //     else
        //     {
        //         response.Approved = false;
        //         response.Reason = "No spawn location available";
        //     }

        // }

        // private void OnClientDisconnectCallback(ulong obj)
        // {
        //     if (!_networkManager.IsServer && _networkManager.DisconnectReason != string.Empty)
        //     {
        //         Debug.Log($"Approval Declined Reason: {_networkManager.DisconnectReason}");
        //     }
        // }


        public Transform UseSpawnLocation()
        {
            foreach (var team in teamSpawnLocations)
            {
                var tr = team.GetNextUnusedPosition();
                if (tr != null)
                    return tr;
            }

            return null;
        }

        public void ReleaseSpawnLocationInTeamAtIndex(int teamIndex, int index) => teamSpawnLocations[teamIndex].ReleasePositionAtIndex(index);

        public void ReleaseSpawnLocation(Transform transform) => teamSpawnLocations.ForEach(team => team.ReleasePositionByTransform(transform));

        public void ReleaseAllSpawnLocations() => teamSpawnLocations.ForEach(team => team.ReleaseAllPositions());

        public bool AllSpawnLocationsUsed() => teamSpawnLocations.TrueForAll(team => team.AllPositionsUsed());

        public bool AllSpawnLocationsUsedInTeam(int teamIndex) => teamSpawnLocations[teamIndex].AllPositionsUsed();


    }

}
