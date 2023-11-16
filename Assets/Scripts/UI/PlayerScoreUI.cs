using Interactions;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace UI
{
    public class PlayerScoreUI : MonoBehaviour
    {
        [field: SerializeField] public ulong ClientId { get; private set; }
        [Header("Client Data")]
        [SerializeField] NetworkPlayerScore playerScore;
        
        [Header("Text fields")]
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text killsText;
        [SerializeField] TMP_Text deathsText;
        [SerializeField] TMP_Text scoreText;
        
        [Header("Local Player Styling")]
        [SerializeField] Image backgroundImage;
        [SerializeField] Color localPlayerColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        
        public void Initialise(NetworkPlayerScore ps)
        { 
            playerScore = ps;
            ps.kills.OnValueChanged += OnKillsChanged;
            ps.deaths.OnValueChanged += OnDeathsChanged;
            ps.score.OnValueChanged += OnScoreChanged;

            var id = ps.OwnerClientId;
            
            if(backgroundImage == null) 
                GetComponent<Image>();
            
            if(NetworkManager.Singleton.LocalClientId == id)
                backgroundImage.color = localPlayerColor;
            
            ClientId = id;
            OnUpdateName($"Player {id}");
            OnScoreChanged(0, ps.score.Value);
            OnDeathsChanged(0, ps.deaths.Value);
            OnKillsChanged(0, ps.kills.Value);
        }

        private void OnScoreChanged(int previousvalue, int newvalue)
        { 
            scoreText.text = newvalue.ToString();
        }

        private void OnDeathsChanged(int previousvalue, int newvalue)
        {
            deathsText.text = newvalue.ToString();
        }

        private void OnKillsChanged(int previousvalue, int newvalue)
        {
            killsText.text = newvalue.ToString();
        }
        
        private void OnUpdateName(string newvalue)
        {
            nameText.text = newvalue;
        }

        private void OnDestroy()
        {
            playerScore.kills.OnValueChanged -= OnKillsChanged;
            playerScore.deaths.OnValueChanged -= OnDeathsChanged;
            playerScore.score.OnValueChanged -= OnScoreChanged;
        }
    }
}