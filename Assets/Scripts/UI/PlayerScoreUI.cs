using TMPro;
using UnityEngine;

namespace UI
{
    public class PlayerScoreUI : MonoBehaviour
    {
        [SerializeField] ClientPlayerScoreData playerData;
        [SerializeField] TMP_Text playerNameText;
        [SerializeField] TMP_Text playerKillsText;
        [SerializeField] TMP_Text playerDeathsText;
        
        public void Initialise(ulong clientId)
        {
            playerData = new ClientPlayerScoreData(clientId);
            playerNameText.text = "Player " + playerData.clientId;
            playerKillsText.text = playerData.kills.ToString();
            playerDeathsText.text = playerData.deaths.ToString();
        }

        public void UpdatePlayerScore(int kills, int deaths)
        {
            playerData.UpdatePlayerScore(kills, deaths);
            playerKillsText.text = playerData.kills.ToString();
            playerDeathsText.text = playerData.deaths.ToString();
        }
        
        public ulong GetClientId => playerData.clientId;
    }
}