using Enums;
using UnityEngine;

namespace PlayerInteraction.Networking.ScriptableObjects
{
    [CreateAssetMenu(fileName = "PlayerData", menuName = "PlayerData", order = 1)]
    public class PlayerData : ScriptableObject
    {
        [field: SerializeField] public Texture Avatar { get; private set; }
        [field: SerializeField] public string Name { get; private set; }
        [field: SerializeField] public Chassis Chassis { get; private set; } = Chassis.None;
        [field: SerializeField] public int Level { get; private set; }
        [field: SerializeField] public int Experience { get; private set; }
        [field: SerializeField] public int Kills { get; private set; }
        [field: SerializeField] public int Deaths { get; private set; }
        [field: SerializeField] public int Assists { get; private set; }
        [field: SerializeField] public int Wins { get; private set; }
        [field: SerializeField] public int Losses { get; private set; }
        [field: SerializeField] public int Draws { get; private set; }
        [field: SerializeField] public int MatchesPlayed { get; private set; }

        // [field: SerializeField] public Team CurrentTeam {get; private set;} = Team.None;
     
        
        
        public void SetPlayerName(string name)
        {
            Name = name;
        }
        
        public void SetPlayerChassis(Chassis chassis)
        {
            Chassis = chassis;
        }
        
        public void SetPlayerLevel(int level)
        {
            Level = level;
        }
        
        public void SetPlayerExperience(int experience)
        {
            Experience = experience;
        }
        
        public void SetPlayerKills(int kills)
        {
            Kills = kills;
        }
        
        public void SetPlayerDeaths(int deaths)
        {
            Deaths = deaths;
        }
        
        public void SetPlayerAssists(int assists)
        {
            Assists = assists;
        }
        
        public void SetPlayerWins(int wins)
        {
            Wins = wins;
        }
        
        public void SetPlayerLosses(int losses)
        {
            Losses = losses;
        }
        
        public void SetPlayerDraws(int draws)
        {
            Draws = draws;
        }
        
        public void SetPlayerMatchesPlayed(int matchesPlayed)
        {
            MatchesPlayed = matchesPlayed;
        }
        
        // public void SetPlayerTeam(Team team)
        // {
        //     CurrentTeam = team;
        // }
        
        public string GetChassisName()
        {
            return Chassis.ToString();
        }
    }
}