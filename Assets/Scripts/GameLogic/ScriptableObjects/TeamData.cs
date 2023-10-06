

using UnityEngine;

public class TeamData : ScriptableObject
{
    [field: SerializeField] public int TeamID { get; private set; }
    [field: SerializeField] public Color TeamColor { get; private set; }

    public void SetTeamID(int id)
    {
        TeamID = id;
        SetTeamColor(id == 0 ? Color.red : Color.blue);
    }

    void SetTeamColor(Color color) => TeamColor = color;

}