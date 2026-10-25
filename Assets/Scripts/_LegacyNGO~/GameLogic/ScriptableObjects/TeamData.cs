

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GameLogic.ScriptableObjects
{
    public class TeamData : ScriptableObject
    {
        [field: SerializeField] public int TeamID { get; private set; }
        [field: SerializeField] public Color TeamColor { get; private set; }
        public List<NetworkClient> Players { get; private set; }

        public void AddPlayer(NetworkClient player)
        {
            Players.Add(player);
        }

        public void RemovePlayer(NetworkClient player)
        {
            Players.Remove(player);
        }

        public void SetTeamColor(Color color)
        {
            TeamColor = color;
        }

        public void SetTeamID(int id)
        {
            TeamID = id;
        }

        public void SetPlayers(List<NetworkClient> players)
        {
            Players = players;
        }
    }
}