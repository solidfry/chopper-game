using System.Collections.Generic;
using GameLogic.ScriptableObjects;
using UI.ScriptableObjects;
using Unity.Netcode;
using UnityEngine;

namespace GameLogic
{
    public static class MatchDataBuilder
    {
        public static MatchData CreateMatchData()
        {
            var matchData = ScriptableObject.CreateInstance<MatchData>();
            return matchData;
        }

        public static MatchData CreateMatchDataAndTeams(List<NetworkClient> players, ColorData teamColors)
        {
            var matchData = ScriptableObject.CreateInstance<MatchData>();
            matchData.AddPlayersFromList(players);
            matchData.GenerateTeams(teamColors);
            return matchData;
        }

        public static MatchData SetPlayers(this MatchData matchData, List<NetworkClient> players)
        {
            matchData.AddPlayersFromList(players);
            return matchData;
        }

        public static MatchData SetGameMode(this MatchData matchData, GameMode gameMode) 
        {
            matchData.SetGameMode(gameMode);
            return matchData;
        }

        public static MatchData SetTeams(this MatchData matchData, ColorData teamColors)
        {
            matchData.GenerateTeams(teamColors);
            return matchData;
        }

        public static List<NetworkClient> AddPlayerClients(IReadOnlyList<NetworkClient> connectedClientsList)
        {
            var players = new List<NetworkClient>();
            for (var i = 0; i < connectedClientsList.Count; i++)
            {
                players.Add(connectedClientsList[i]);
            }

            return players;
        }
    }
}