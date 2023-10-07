using System.Collections.Generic;
using DG.Tweening;
using Enums;
using Player.Networking.ScriptableObjects;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Utilities;

public class PlayerProfileUI : MonoBehaviour
{
    [SerializeField] [ReadOnly] private Color teamColor = new ();
    [Header("Player Data")]
    [SerializeField] Team team;
    [SerializeField] PlayerData playerData;
    [SerializeField] ColorData colours;
    
    [Header("Text Components")]
    [SerializeField] TMP_Text playerName;
    [SerializeField] TMP_Text playerClass;
    [SerializeField] TMP_Text playerLevel;
    [SerializeField] TMP_Text levelLabel;
    
    private List<TMP_Text> _textComponents;
    [Header("Image Components")]
    [SerializeField] RawImage avatar;
    [SerializeField] Image background;
    
    [Header("Animation values")]
    [SerializeField] float animationDuration = 1f;
    [SerializeField] float animationDelay = 0.5f;
    [SerializeField] float animationXOffset = -100f;
    [SerializeField] Ease animationEase = Ease.OutExpo;

    CanvasGroup _canvasGroup;
    
    [Header("Checks")]
    [SerializeField][ReadOnly] private bool isInitialised = false;
    [SerializeField][ReadOnly] bool colourIsSet = false;
    bool Initialised => isInitialised && colourIsSet;
    
    private void Start()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 0;
        if(playerData != null)
        {
            CompileTextComponents();
            SetPlayerValues(playerData);
            UpdateColours();
            
            if(Initialised)
                Show();
        }
    }
    
    private void OnDisable() => DOTween.KillAll();

    private void SetPlayerValues(PlayerData data)
    {
        SetPlayerName(data.Name);
        SetPlayerClass(data.GetChassisName());
        SetPlayerLevel(data.Level);
        SetAvatar(data.Avatar);
        SetTeam(data.CurrentTeam);
        isInitialised = true;
    }
    
    public void UpdateColours()
    {
        if (!isInitialised) return;

        var teamName = team.ToString();
        teamColor = colours.GetColourByName(teamName);
        
        foreach (var text in _textComponents)
        {
            text.color = teamColor;
            Debug.Log(teamColor);
        }

        if (background != null) 
            background.color = teamColor;
        
        colourIsSet = true;
    }

    private void CompileTextComponents() => _textComponents = new List<TMP_Text> { playerName, playerClass, playerLevel };

    private void SetPlayerName(string name)
    {
        playerName.text = name;
    }
    
    private void SetPlayerClass(string playerClass)
    {
        this.playerClass.text = playerClass;
    }
    
    private void SetPlayerLevel(int level)
    {
        playerLevel.text = level.ToString();
    }
    
    private void SetAvatar(Texture image)
    {
        avatar.texture = image;
    }

    private void SetTeam(Team currentTeam) => team = currentTeam;

    private void Show()
    {
        _canvasGroup.DOFade(1, animationDuration).SetDelay(animationDelay).From(0);
        transform.DOLocalMoveX(0, animationDuration).SetDelay(animationDelay).From(animationXOffset).SetEase(animationEase);
    }
}
