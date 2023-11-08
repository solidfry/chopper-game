using System.Collections;
using System.Collections.Generic;
using Events;
using PlayerInteraction.Networking;
using UnityEngine;

namespace Networking
{
    public class ServerSpawnManager : SingletonNetwork<ServerSpawnManager>
    {
        [SerializeField] PlayerManager playerPrefab;
        [SerializeField] float respawnTime = 5f;
        [SerializeField] SpawnLocations spawnLocations = new();
        [SerializeField] ParticleSystem respawnEffect;
        
        public int GetSpawnLocationsCount => spawnLocations.spawnLocations.Count;
        
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
            GetRandomSpawnLocation(out var spawnLocation);
            if (spawnLocation == null) return;
            StartCoroutine(RespawnPlayerCoroutine(clientid, spawnLocation));
            Debug.Log("Player was moved to spawn location");
        }
        
        IEnumerator RespawnPlayerCoroutine(ulong clientid, Transform spawnLocation)
        {
            yield return new WaitForSeconds(respawnTime);
            RespawnPlayer(clientid, spawnLocation);
            // if(respawnEffect)
            //     respawnEffect.Play();
        }
        
        void RespawnPlayer(ulong clientid, Transform spawnLocation)
        {
            NetworkManager.ConnectedClients[clientid].PlayerObject.TryGetComponent(out PlayerManager playerManager);
            
            if (playerManager == null) return;
            playerManager.PositionPlayerClientRpc(spawnLocation.position, spawnLocation.rotation);
            ReleaseSpawnLocation(spawnLocation);
        }

       private Transform UseSpawnLocation()
        {
            var tr = spawnLocations.GetNextUnusedPosition();
            if (tr != null)
                return tr;
            return null;
        }
        
        public PlayerManager GetPlayerPrefab() => playerPrefab;
        public void GetSpawnLocation(out Transform spawnLocation) => spawnLocation = UseSpawnLocation();
        void GetRandomSpawnLocation(out Transform spawnLocation) => spawnLocation = spawnLocations.GetRandomUnusedPosition();
        
        void ReleaseSpawnLocation(Transform tr) => StartCoroutine(ReleaseSpawnLocationCoroutine(tr));

        private IEnumerator ReleaseSpawnLocationCoroutine(Transform tr)
        {
            yield return new WaitForSeconds(respawnTime);
            spawnLocations.ReleasePositionByTransform(tr);
        }

        public void ReleaseAllSpawnLocations() => spawnLocations.ReleaseAllPositions();
    }

}
