using System.Collections.Generic;
using Player.Networking;
using Unity.Netcode;
using UnityEngine;
using System.Linq;

namespace GameLogic.ScriptableObjects
{
    public class MatchData : ScriptableObject
    {
        [field: SerializeField] public int MatchID { get; private set; }
        public Dictionary<NetworkObject, PlayerManager> Players { get; private set; } = new();
        [field: SerializeField] public TeamData[] Teams { get; private set; } = new TeamData[2];

        public void GenerateTeams(Color[] teamColors)
        {
            if (Players.Count == 0) return;
            
            Teams = TeamDataBuilder.SplitPlayersIntoTwoTeams(Players.Keys.ToList(), teamColors);
        }

        public void AddPlayer(NetworkObject player, PlayerManager playerManager)
        {
            if (Players.ContainsKey(player)) return;
            Players.Add(player, playerManager);
        }
        
        public void RemovePlayer(NetworkObject player)
        {
            if (!Players.ContainsKey(player)) return;
            Players.Remove(player);
        }
        
        public void AddPlayersFromList(List<NetworkObject> players)
        {
            foreach (var player in players)
            {
                if (Players.ContainsKey(player)) continue;
                Players.Add(player, player.GetComponent<PlayerManager>());
            }
        }
        
        public void SetMatchID(int id)
        {
            MatchID = id;
        }
        
    }
}
