using System.Collections.Generic;
using GameLogic.ScriptableObjects;
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
        
        public static MatchData CreateMatchDataAndTeams(List<NetworkObject> players, Color[] teamColors)
        {
            var matchData = ScriptableObject.CreateInstance<MatchData>();
            matchData.AddPlayersFromList(players);
            matchData.GenerateTeams(teamColors);
            return matchData;
        }
    }
}