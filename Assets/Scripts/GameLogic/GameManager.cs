using System.Collections.Generic;
using GameLogic.ScriptableObjects;
using PlayerInteraction.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public class GameManager : SingletonPersistent<GameManager>
    {
        [SerializeField] NetworkManager networkManagerPrefab;
        [SerializeField] MatchData matchData;
        
        
        public override void Awake()
        {
            base.Awake();

            if (NetworkManager.Singleton == null)
            {
                Debug.Log("NetworkManager prefab spawned");
                Instantiate(networkManagerPrefab);
            }
        }
        
        public void SetMatchData(MatchData data) => matchData = data;
        public ushort GetMaxPlayersPerTeam() => matchData.GameMode.MaxPlayersPerTeam;
        public ushort GetMaxTeams() => matchData.GameMode.MaxTeams;
        public MatchData GetMatchData() => matchData;
        public GameMode GetGameMode() => matchData.GameMode;
        public TeamData[] GetTeams() => matchData.Teams;
        public int GetMatchID() => matchData.MatchID;
        public Dictionary<NetworkClient, PlayerManager> GetPlayers() => matchData.Players;

    }
}