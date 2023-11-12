using UnityEngine;

namespace GameLogic.ScriptableObjects
{
    [CreateAssetMenu(fileName = "GameMode", menuName = "GameMode", order = 0)]
    public class GameMode : ScriptableObject
    {
        [SerializeField] private string gameModeName;
        [SerializeField] private ushort maxTeams;
        [SerializeField] private ushort maxPlayersPerTeam;
        [SerializeField] private ushort maxTotalKills;
        [SerializeField] private ushort gameStartPlayerCount;
        [SerializeField] private float preGameCountdownTime;
        [SerializeField] private float gameStartCountdownTime;
    
        public string GameModeName => gameModeName;
        public ushort MaxTeams => maxTeams;
        public ushort MaxPlayersPerTeam => maxPlayersPerTeam;
        public ushort MaxTotalKills => maxTotalKills;
        public ushort GameStartPlayerCount => gameStartPlayerCount;
        public float PreGameCountdownTime => preGameCountdownTime;
        public float GameStartCountdownTime => gameStartCountdownTime;

        public override string ToString() => $"{nameof(gameModeName)}: {gameModeName}, {nameof(maxTeams)}: {maxTeams}, {nameof(maxPlayersPerTeam)}: {maxPlayersPerTeam}";


    }
}