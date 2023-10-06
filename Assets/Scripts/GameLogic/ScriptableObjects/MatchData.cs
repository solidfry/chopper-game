using System.Collections.Generic;
using Player.Networking;
using Unity.Netcode;
using UnityEngine;

public class MatchData : ScriptableObject
{
    [field: SerializeField] public int MatchID { get; private set; }
    [field: SerializeField] public Dictionary<NetworkObject, PlayerManager> players { get; private set; }
    [field: SerializeField] public TeamData[] teams { get; private set; } = new TeamData[2];


    public void GenerateTeams()
    {
        for (int i = 0; i < teams.Length; i++)
        {
            teams[i] = ScriptableObject.CreateInstance<TeamData>();
        }

    }


}
