using System.Collections.Generic;
using Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace UI
{
    public class ScoreBoardUIHandler : MonoBehaviour
    {
        [Header("Player Scores")]
        [SerializeField] List<PlayerScoreUI> playerScores = new ();
    
        [Header("Scoreboard UI")]
        [SerializeField] RectTransform scoreboardObjectRect;
        [SerializeField] RectTransform scoreboardPlayerListRect;
        [SerializeField] PlayerScoreUI playerScoreUIPrefab;
    
        [Header("Show Scoreboard Input")]
        [SerializeField] InputAction showScoreboardAction;
    
        [Space]
        [Header("Unity Events")]
        [Space]
        [SerializeField] UnityEvent onScoreboardShown;
        [SerializeField] UnityEvent onScoreboardHidden;
    
        private void OnEnable()
        {
            if (ObjectsAreNull)
            {
                Debug.LogWarning("Scoreboard UI Handler is missing references");
                return;
            }
            
            scoreboardObjectRect.gameObject.SetActive(true);
        
            showScoreboardAction.Enable();

            showScoreboardAction.performed += ToggleScoreboard;
            showScoreboardAction.canceled += ToggleScoreboard;
            GameEvents.OnUpdatePlayerScoreEvent += UpdatePlayerScore;
            GameEvents.OnRemovePlayerScoreEvent += RemovePlayerScore;
        }
        
        private void OnDisable()
        {
            if (ObjectsAreNull) return;

            showScoreboardAction.Disable();

            showScoreboardAction.performed -= ToggleScoreboard;
            showScoreboardAction.canceled -= ToggleScoreboard;
            GameEvents.OnUpdatePlayerScoreEvent -= UpdatePlayerScore;
            GameEvents.OnRemovePlayerScoreEvent -= RemovePlayerScore;
        }
    
        bool ObjectsAreNull => scoreboardObjectRect == null || scoreboardPlayerListRect == null || playerScoreUIPrefab == null;

        private void RemovePlayerScore(ulong clientId)
        {
            if (playerScores.Count == 0) return;
            foreach (var playerScore in playerScores)
            {
                if (playerScore.GetClientId == clientId)
                {
                    playerScores.Remove(playerScore);
                    Destroy(playerScore.gameObject);
                    return;
                }
            }
        }
        
        private void UpdatePlayerScore(ulong clientId, int kills, int deaths)
        {
            if(playerScores.Find(c => c.GetClientId == clientId) is null)
            {
                AddPlayerScoreUI(clientId);
            }
            else
            {
                playerScores.Find(c => c.GetClientId == clientId).UpdatePlayerScore(kills, deaths);
            }
        }

        private void ToggleScoreboard(InputAction.CallbackContext ctx)
        {
            switch (ctx)
            {
                case { performed: true }:
                    onScoreboardShown?.Invoke();
                    break;
                case { canceled: true }:
                    onScoreboardHidden?.Invoke();
                    break;
            }
        }
    
        void AddPlayerScoreUI(ulong clientId)
        {
            var playerScoreUI = Instantiate(playerScoreUIPrefab, scoreboardPlayerListRect);
            playerScoreUI.Initialise(clientId);
        }
        
    }
}
