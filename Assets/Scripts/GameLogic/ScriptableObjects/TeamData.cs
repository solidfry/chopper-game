

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace GameLogic.ScriptableObjects
{
    public class TeamData : ScriptableObject
    {
        [field: SerializeField] public int TeamID { get; private set; }
        [field: SerializeField] public Color TeamColor { get; private set; }
        public HashSet<NetworkObject> Players { get; private set; }
        
        public void AddPlayer(NetworkObject player)
        {
            Players.Add(player);
        }
        
        public void RemovePlayer(NetworkObject player)
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
        
        public void SetPlayers(HashSet<NetworkObject> players)
        {
            Players = players;
        }
    }
}