using System.Collections.Generic;
using GameLogic.ScriptableObjects;
using UI.ScriptableObjects;
using Unity.Netcode;
using System.Linq;
using UnityEngine;
using Utilities;

namespace GameLogic
{
    public static class TeamDataBuilder
    {

        public static TeamData[] SplitPlayersIntoTwoTeams(List<NetworkClient> players, ColourDataList teamColours)
        {
            var teamData = new TeamData[2];

            for (int i = 0; i < teamData.Length; i++)
            {
                teamData[i] = ScriptableObject.CreateInstance<TeamData>();
                teamData[i].SetTeamColor(teamColours.GetColourByIndex(i));
                teamData[i].SetTeamID(i);
                teamData[i].SetPlayers(players.Where((_, index) => index % 2 == i).ToList());
            }

            return teamData;
        }

        public static TeamData CreateTeam() => ScriptableObject.CreateInstance<TeamData>();

        public static TeamData AddPlayers(this TeamData teamData, List<NetworkClient> players)
        {
            teamData.SetPlayers(players);
            return teamData;
        }

        public static TeamData SetTeamColor(this TeamData teamData, Color color)
        {
            teamData.SetTeamColor(color);
            return teamData;
        }



    }
}