using System.Collections.Generic;
using DG.Tweening;
using Enums;
using Player.Networking.ScriptableObjects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
public class PlayerProfileUI : MonoBehaviour
{
    [SerializeField] [ReadOnly] private Color teamColor = new ();
    [Header("Player Data")]
    [SerializeField] Team team;
    [SerializeField] PlayerData playerData;
    
    [Header("Text Components")]
    [SerializeField] TMP_Text playerName;
    [SerializeField] TMP_Text playerClass;
    [SerializeField] TMP_Text playerLevel;
    [SerializeField] TMP_Text levelLabel;
    
    private List<TMP_Text> _textComponents;
    [Header("Image Components")]
    [SerializeField] PlayerAvatarImage playerAvatarImage;
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
        // Initialise(playerData);
    }
    
    private void OnDisable() => DOTween.KillAll();

    public void Initialise(PlayerData data, Team currentTeam, Color color)
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 0;
        CompileTextComponents();

        if(data != null)
        {
            teamColor = color;
            SetPlayerValues(data);
            UpdateColours(teamColor);
            
            if(Initialised)
                Show();
        }
        else
        {
            // empty placeholder version of the UI
            Debug.Log("No player data found");
            ShowWaitingState();
            Show();
        }
    }
    
    private void SetPlayerValues(PlayerData data)
    {
        SetPlayerName(data.Name);
        SetPlayerClass(data.GetChassisName());
        SetPlayerLevel(data.Level);
        SetAvatar(data.Avatar);
        SetTeam(team);
        isInitialised = true;
    }
    
    public void UpdateColours(Color color)
    {
        if (!isInitialised) return;
        
        foreach (var text in _textComponents)
        {
            text.color = color;
        }

        if (background != null) 
            background.color = color;
        
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
    
    private void SetAvatar(Texture image) => playerAvatarImage.SetAvatar(image);

    private void SetTeam(Team currentTeam) => team = currentTeam;

    private void Show()
    {
        _canvasGroup.DOFade(1, animationDuration).SetDelay(animationDelay).From(0);
        transform.DOLocalMoveX(0, animationDuration).SetDelay(animationDelay).From(animationXOffset).SetEase(animationEase);
    }
    
    private void ShowWaitingState()
    {
        
    }
}
