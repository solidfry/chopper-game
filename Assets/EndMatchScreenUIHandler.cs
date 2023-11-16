using DG.Tweening;
using Events;
using Interactions;
using UI;
using Unity.Netcode;
using UnityEngine;

public class EndMatchScreenUIHandler : MonoBehaviour
{
    [SerializeField] StatTile killsStatTile;
    [SerializeField] StatTile deathsStatTile;
    [SerializeField] StatTile scoreStatTile;
    
    [SerializeField] DOTweenAnimation[] animations;

    private void OnEnable()
    {
        NetworkPlayerScore.OnPlayerSpawnedEvent += SetScore;
        GameEvents.OnShowEndGameScreenEvent += ShowEndMatchScreen;
    }

    private void OnDisable()
    {
        NetworkPlayerScore.OnPlayerSpawnedEvent -= SetScore;
        GameEvents.OnShowEndGameScreenEvent -= ShowEndMatchScreen;
    }

    [ContextMenu("ShowEndMatchScreen")]
    public void ShowEndMatchScreen()
    {
        if(animations.Length > 0)
            foreach (var anim in animations)
            {
                anim.DOPlay();
            }
    }
    
    public void SetScore(NetworkPlayerScore playerScore)
    {
        if(NetworkManager.Singleton.LocalClientId == playerScore.OwnerClientId)
            SubscribeToValues(playerScore);
    }

    private void SubscribeToValues(NetworkPlayerScore playerScore)
    {
        playerScore.kills.OnValueChanged += UpdateKills;
        playerScore.deaths.OnValueChanged += UpdateDeaths;
        playerScore.score.OnValueChanged += UpdateScore;
        UpdateScore(0, playerScore.score.Value);
        UpdateDeaths(0, playerScore.deaths.Value);
        UpdateKills(0, playerScore.kills.Value);
    }
    
    private void UpdateScore(int previousvalue, int newvalue)
    {
        scoreStatTile.SetStat(newvalue.ToString());
    }

    private void UpdateDeaths(int previousvalue, int newvalue)
    {
        deathsStatTile.SetStat(newvalue.ToString());
    }

    private void UpdateKills(int previousvalue, int newvalue)
    {
        killsStatTile.SetStat(newvalue.ToString());
    }
}
