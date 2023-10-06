using System.Collections.Generic;
using GameLogic.ScriptableObjects;
using Unity.Netcode;
using UnityEngine;

namespace GameLogic
{
    public static class TeamDataBuilder
    {
        
        public static TeamData[] SplitPlayersIntoTwoTeams(List<NetworkObject> players, Color[] teamColors)
        {
            var teamData = new TeamData[2]; // will this add 2 items or 3? 
            
            for (int i = 0; i < teamData.Length; i++)
            {
                teamData[i] = ScriptableObject.CreateInstance<TeamData>();
                teamData[i].SetTeamColor(teamColors[i]);
                teamData[i].SetTeamID(i);
                teamData[i].SetPlayers(new HashSet<NetworkObject>());
            }
            
            return teamData;
        }
        


    }
}