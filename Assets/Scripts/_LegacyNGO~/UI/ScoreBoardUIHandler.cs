using System.Collections.Generic;
using Interactions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace UI
{
    public class ScoreBoardUIHandler : MonoBehaviour
    {
        [Header("Player Scores Templates")]
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
            NetworkPlayerScore.OnPlayerSpawnedEvent += PlayerSpawned;
            NetworkPlayerScore.OnPlayerDespawnedEvent += PlayerDespawned;
            showScoreboardAction.performed += ToggleScoreboard;
            showScoreboardAction.canceled += ToggleScoreboard;
        }

        private void PlayerDespawned(NetworkPlayerScore obj) => RemovePlayerScore(obj.OwnerClientId);

        private void PlayerSpawned(NetworkPlayerScore obj) => AddPlayerScoreUI(obj);

        private void OnDisable()
        {
            if (ObjectsAreNull) return;

            showScoreboardAction.Disable();
            NetworkPlayerScore.OnPlayerSpawnedEvent -= PlayerSpawned;
            NetworkPlayerScore.OnPlayerDespawnedEvent -= PlayerDespawned;
            showScoreboardAction.performed -= ToggleScoreboard;
            showScoreboardAction.canceled -= ToggleScoreboard;
        }
    
        bool ObjectsAreNull => scoreboardObjectRect == null || scoreboardPlayerListRect == null || playerScoreUIPrefab == null;

        private void RemovePlayerScore(ulong clientId)
        {
            var playerScore = playerScores.Find(c => c.ClientId == clientId);
            if (playerScore is not null)
            {
                playerScores.Remove(playerScore);
                Destroy(playerScore.gameObject);
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
    
        void AddPlayerScoreUI(NetworkPlayerScore playerScore)
        {
            var playerScoreUI = Instantiate(playerScoreUIPrefab, scoreboardPlayerListRect);
            playerScoreUI.Initialise(playerScore);
            playerScores.Add(playerScoreUI);
        }
        
    }
}
