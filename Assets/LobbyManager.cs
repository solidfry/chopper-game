using System;
using System.Collections.Generic;
using Enums;
using GameLogic;
using Player.Networking.ScriptableObjects;
using UnityEngine;
using Utilities;

public class LobbyManager : MonoBehaviour
{
    // SingletonClass 
    private static LobbyManager _instance;
    public static LobbyManager Instance => _instance;
    
    [SerializeField] ProfileLoader profileLoaderPrefabLeft;
    [SerializeField] ProfileLoader profileLoaderPrefabRight;
    
    List<ProfileLoader> profiles = new ();
    
    // [SerializeField] List<ProfileLoader> players = new ();
    [SerializeField] List<PlayerData> playerData = new ();

    [SerializeField] private GameObject teamAInstance;
    [SerializeField] private GameObject teamBInstance;
    

    [SerializeField] ColorData colours;

    private void Start()
    {
        if (_instance != null && _instance != this) Destroy(gameObject);
        else _instance = this;

        AddPlayersToTeamList(playerData);
    }
    
    public void AddPlayer(PlayerData data)
    {
        playerData.Add(data);
    }
    
    public void RemovePlayer(PlayerData data)
    {
        playerData.Remove(data);
    }
    
    void AddPlayersToTeamList(List<PlayerData> players)
    {
        for (int i = 0; i < players.Count; i++)
            if (i % 2 == 0)
                InstantiateProfileLoader(profileLoaderPrefabLeft, teamAInstance.transform, players[i], SelectTeam());
            else
                InstantiateProfileLoader(profileLoaderPrefabRight, teamBInstance.transform, players[i], SelectTeam());
    }
    
    // If the max number of players has not been reached the Lobby will wait for more players to join and then update the UI as they join. 
    
    
    public void AddPlayerToTeamList(PlayerData player)
    {
        if (profiles.Count == 0)
        {
            InstantiateProfileLoader(profileLoaderPrefabLeft, teamAInstance.transform, player, SelectTeam());
            return;
        }
        
        if (profiles.Count % 2 == 0)
        {
            InstantiateProfileLoader(profileLoaderPrefabLeft, teamAInstance.transform, player, SelectTeam());
        }
        else
        {
            InstantiateProfileLoader(profileLoaderPrefabRight, teamBInstance.transform, player, SelectTeam());
        }
    }
    
    Team SelectTeam()
    {
        if (profiles.Count == 0) return Team.A;
        
        if (profiles.Count % 2 == 0)
            return Team.A;
        
        return Team.B;
    }
    
    void InstantiateProfileLoader(ProfileLoader prefab, Transform parent, PlayerData data, Team team)
    {
        Color teamColor = colours.GetColourByName(team.ToString());
        ProfileLoader profileLoader = Instantiate(prefab, parent).GetComponent<ProfileLoader>();
        profileLoader.ClearDummyData();
        profileLoader.SetPlayerData(data, team, teamColor);
        profiles.Add(profileLoader);
    }
    
    // public void UpdateProfiles()
    // {
    //     if (playerData.Count == 0) return;
    //     
    //     var teamColors = TeamDataBuilder.GetTeamColors(colours);
    //     var teams = TeamDataBuilder.SplitPlayersIntoTwoTeams(playerData, teamColors);
    //     
    //     foreach (var player in teams[0])
    //     {
    //         var profileLoader = Instantiate(profileLoaderPrefabLeft, teamAInstance.transform);
    //         profileLoader.SetPlayerData(player);
    //     }
    //     
    //     foreach (var player in teams[1])
    //     {
    //         var profileLoader = Instantiate(profileLoaderPrefabRight, teamBInstance.transform);
    //         profileLoader.SetPlayerData(player);
    //     }
    // }
    
    
}
