using System.Collections.Generic;
using Enums;
using PlayerInteraction.Networking.ScriptableObjects;
using UI;
using UI.ScriptableObjects;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace Networking
{
    public class LobbyManager : NetworkBehaviour
    {
        // SingletonClass 

        [SerializeField] ProfileLoader profileLoaderPrefabLeft;
        [SerializeField] ProfileLoader profileLoaderPrefabRight;

        List<ProfileLoader> profiles = new();

        // [SerializeField] List<ProfileLoader> players = new ();
        [SerializeField] List<PlayerData> playerData = new();

        // [SerializeField] private GameObject teamAGridLocation;
        // [SerializeField] private GameObject teamBGridLocation;
        
        [SerializeField] ColourDataList colours;

        [SerializeField] private NetworkManager networkManager;

        private void Start()
        {
            networkManager = NetworkManager.Singleton;
            foreach (var client in networkManager.ConnectedClients)
            {
                AddPlayer(client.Value.ClientId);
            }
        }

        private void OnEnable()
        {
            networkManager.OnClientConnectedCallback += AddPlayer;
            networkManager.OnClientDisconnectCallback += RemovePlayer;
        }

        private void OnDisable()
        {
            networkManager.OnClientConnectedCallback -= AddPlayer;
            networkManager.OnClientDisconnectCallback -= RemovePlayer;
        }

        // private void Start()
        // {
        //     AddPlayersToTeamList(playerData);
        // }

        public void AddPlayer(ulong data)
        {
            // playerData.Add(data);
            Debug.Log("Player was added");
        }

        public void RemovePlayer(ulong data)
        {
            // playerData.Remove(data);
        }

        // void AddPlayersToTeamList(List<PlayerData> players)
        // {
        //     for (int i = 0; i < players.Count; i++)
        //         if (i % 2 == 0)
        //             InstantiateProfileLoader(profileLoaderPrefabLeft, teamAGridLocation.transform, players[i], SelectTeam());
        //         else
        //             InstantiateProfileLoader(profileLoaderPrefabRight, teamBGridLocation.transform, players[i], SelectTeam());
        // }
        //
        // // If the max number of players has not been reached the Lobby will wait for more players to join and then update the UI as they join. 
        //
        //
        // public void AddPlayerToTeamList(PlayerData player)
        // {
        //     if (profiles.Count == 0)
        //     {
        //         InstantiateProfileLoader(profileLoaderPrefabLeft, teamAGridLocation.transform, player, SelectTeam());
        //         return;
        //     }
        //
        //     if (profiles.Count % 2 == 0)
        //     {
        //         InstantiateProfileLoader(profileLoaderPrefabLeft, teamAGridLocation.transform, player, SelectTeam());
        //     }
        //     else
        //     {
        //         InstantiateProfileLoader(profileLoaderPrefabRight, teamBGridLocation.transform, player, SelectTeam());
        //     }
        // }
        //
        // Team SelectTeam()
        // {
        //     if (profiles.Count == 0) return Team.A;
        //
        //     if (profiles.Count % 2 == 0)
        //         return Team.A;
        //
        //     return Team.B;
        // }

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
}
