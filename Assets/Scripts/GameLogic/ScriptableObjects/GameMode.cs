using UnityEngine;

[CreateAssetMenu(fileName = "GameMode", menuName = "GameMode", order = 0)]
public class GameMode : ScriptableObject
{
    [SerializeField] private string gameModeName;
    [SerializeField] private ushort maxTeams;
    [SerializeField] private ushort maxPlayersPerTeam;



    public string GameModeName => gameModeName;
    public ushort MaxTeams => maxTeams;
    public ushort MaxPlayersPerTeam => maxPlayersPerTeam;

    public override string ToString() => $"{nameof(gameModeName)}: {gameModeName}, {nameof(maxTeams)}: {maxTeams}, {nameof(maxPlayersPerTeam)}: {maxPlayersPerTeam}";


}