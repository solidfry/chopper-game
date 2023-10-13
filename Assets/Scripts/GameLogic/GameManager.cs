
using System.Collections.Generic;
using GameLogic;
using GameLogic.ScriptableObjects;
using PlayerInteraction.Networking;
using Unity.Netcode;
using UnityEngine;

public class GameManager : SingletonNetworkPersistent<GameManager>
{
    [SerializeField] MatchData matchData;

    public void SetMatchData(MatchData data) => matchData = data;
    public ushort GetMaxPlayersPerTeam() => matchData.GameMode.MaxPlayersPerTeam;
    public ushort GetMaxTeams() => matchData.GameMode.MaxTeams;
    public MatchData GetMatchData() => matchData;
    public GameMode GetGameMode() => matchData.GameMode;
    public TeamData[] GetTeams() => matchData.Teams;
    public int GetMatchID() => matchData.MatchID;
    public Dictionary<NetworkClient, PlayerManager> GetPlayers() => matchData.Players;

}