using System;
using DG.Tweening;
using Enums;
using PlayerInteraction.Networking.ScriptableObjects;
using UnityEngine;

public class ProfileLoader : MonoBehaviour
{
    [SerializeField] PlayerProfileUI profilePrefab;
    [SerializeField] private PlayerData playerData;
    [SerializeField] private PlayerProfileUI _profileInstance;
    [SerializeField] private GameObject _loadingInstance;
    private Tween _tween;
    
    public event Action<PlayerData, Team, Color> OnProfileReceived;

    private void OnEnable()
    {
        OnProfileReceived += HideAndDestroyLoadingInstance;
    }
    
    private void OnDisable()
    {
        OnProfileReceived -= HideAndDestroyLoadingInstance;
        
        if(_tween != null) 
            _tween.Kill();
    }

    void LoadPlayerProfile(PlayerData data, Team team, Color color )
    {
        _profileInstance = Instantiate(profilePrefab, transform);
        _profileInstance.Initialise(data, team, color);
    }


    public void SetPlayerData(PlayerData data, Team team, Color teamColor)
    {
        playerData = data;
        if(data != null)
            OnProfileReceived?.Invoke(playerData, team, teamColor);
    }
    
    public PlayerProfileUI GetProfileInstance()
    {
        if (playerData != null)
            return _profileInstance;

        return null;
    }
    
    public void ClearDummyData()
    {
        playerData = null;
    }

    void HideAndDestroyLoadingInstance(PlayerData data, Team team, Color color) =>
        _tween = _loadingInstance.GetComponent<CanvasGroup>().DOFade(0, 0.25f).OnComplete(() =>
        {
            Destroy(_loadingInstance);
            LoadPlayerProfile(data, team, color);
        });
}
