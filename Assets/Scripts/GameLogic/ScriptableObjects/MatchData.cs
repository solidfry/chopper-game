using System.Collections.Generic;
using PlayerInteraction.Networking;
using Unity.Netcode;
using UnityEngine;
using System.Linq;
using System;
using UI.ScriptableObjects;

namespace GameLogic.ScriptableObjects
{
    [CreateAssetMenu(fileName = "MatchData", menuName = "MatchData", order = 1)]
    public class MatchData : ScriptableObject
    {
        [field: SerializeField] public int MatchID { get; private set; }
        public Dictionary<NetworkClient, PlayerManager> Players { get; private set; } = new();
        [field: SerializeField] public TeamData[] Teams { get; private set; }
        [field: SerializeField] public GameMode GameMode { get; private set; }

        public void SetGameMode(GameMode gameMode)
        {
            GameMode = gameMode;
            if (gameMode == null) return;
            Teams = new TeamData[gameMode.MaxTeams];
        }

        public void GenerateEmptyTeams()
        {
            if (Players.Count == 0) return;

            for (var i = 0; i < Teams.Length; i++)
            {
                Teams[i] = TeamDataBuilder.CreateTeam();
            }
        }

        public void GenerateTeams(ColorData teamColors)
        {
            if (Players.Count == 0) return;

            Teams = TeamDataBuilder.SplitPlayersIntoTwoTeams(Players.Keys.ToList(), teamColors);
        }

        public void AddPlayer(NetworkClient player, PlayerManager playerManager)
        {
            if (Players.ContainsKey(player)) return;
            Players.Add(player, playerManager);
        }

        public void RemovePlayer(NetworkClient player)
        {
            if (!Players.ContainsKey(player)) return;
            Players.Remove(player);
        }

        public void AddPlayersFromList(List<NetworkClient> players)
        {
            foreach (var player in players)
            {
                if (Players.ContainsKey(player)) continue;

                Players.Add(player, player.PlayerObject.GetComponent<PlayerManager>());
            }
        }

        public void SetMatchID(int id)
        {
            MatchID = id;
        }
        
        void AddPlayerToTeam(NetworkClient player)
        {
            if (Teams == null || Teams.Length == 0) return;

            var teamWithLeastPlayers = Teams.OrderBy(team => team.Players.Count).First();
            teamWithLeastPlayers.AddPlayer(player);
        }

    }
}
