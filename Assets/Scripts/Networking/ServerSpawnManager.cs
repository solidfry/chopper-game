using System.Collections;
using System.Collections.Generic;
using Events;
using Networking.Spawns;
using PlayerInteraction.Networking;
using UnityEngine;

namespace Networking
{
    public class ServerSpawnManager : SingletonNetwork<ServerSpawnManager>
    {
        [SerializeField] PlayerManager playerPrefab;
        [SerializeField] List<TeamSpawnLocations> teamSpawnLocations = new();
        [SerializeField] float respawnTime = 5f;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if(!IsServer) return;
            GameEvents.OnPlayerDiedEvent += OnPlayerDied;
            GameEvents.OnStartMatchEvent += OnStartMatch;
        }
        
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if(!IsServer) return;
            GameEvents.OnPlayerDiedEvent -= OnPlayerDied;
            GameEvents.OnStartMatchEvent -= OnStartMatch;
        }

        void OnStartMatch() => ReleaseAllSpawnLocations();

        void OnPlayerDied(ulong clientid)
        {
            if (!IsServer) return;
            GetSpawnLocation(out var spawnLocation);
            RespawnPlayer(clientid, spawnLocation);
            Debug.Log("Player was moved to spawn location");
        }
        
        void RespawnPlayer(ulong clientid, Transform spawnLocation)
        {
            NetworkManager.ConnectedClients[clientid].PlayerObject.GetComponent<PlayerManager>().PositionPlayerClientRpc(spawnLocation.position, spawnLocation.rotation);
            Debug.Log("Player should be moved");
            ReleaseSpawnLocation(spawnLocation);
        }

       private Transform UseSpawnLocation()
        {
            foreach (var team in teamSpawnLocations)
            {
                var tr = team.GetNextUnusedPosition();
                if (tr != null)
                    return tr;
            }

            return null;
        }
        
        public PlayerManager GetPlayerPrefab() => playerPrefab;
        public void GetSpawnLocation(out Transform spawnLocation) => spawnLocation = UseSpawnLocation();

        public void ReleaseSpawnLocationInTeamAtIndex(int teamIndex, int index) => teamSpawnLocations[teamIndex].ReleasePositionAtIndex(index);

        public void ReleaseSpawnLocation(Transform tr)
        {
            StartCoroutine(ReleaseSpawnLocationCoroutine(tr));
        }

        private IEnumerator ReleaseSpawnLocationCoroutine(Transform tr)
        {
            yield return new WaitForSeconds(respawnTime);
            teamSpawnLocations.ForEach(team => team.ReleasePositionByTransform(tr));
        }

        public void ReleaseAllSpawnLocations() => teamSpawnLocations.ForEach(team => team.ReleaseAllPositions());

        public bool AllSpawnLocationsUsed() => teamSpawnLocations.TrueForAll(team => team.AllPositionsUsed());

        public bool AllSpawnLocationsUsedInTeam(int teamIndex) => teamSpawnLocations[teamIndex].AllPositionsUsed();
        
    }

}
