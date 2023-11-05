using TMPro;
using Unity.Multiplayer.Tools.NetStatsMonitor;
using UnityEngine;

namespace UI
{
    public class TimerUI : MonoBehaviour
    {
        [SerializeField] bool runTimer = false;
        [SerializeField] TMP_Text timerText;
        [SerializeField][Range(0,1)] float monoSpace = 0.8f;
        float currentTimeRemaining;
        
        
        public void StartTimer()
        {
            runTimer = true;
        }
        
        void FormatTimer(float time)
        {
            int minutes = Mathf.FloorToInt(time / 60F);
            int seconds = Mathf.FloorToInt(time - minutes * 60);
            timerText.text = $"<mspace={monoSpace}em>{minutes:00}</mspace>:<mspace={monoSpace}em>{seconds:00}</mspace>";
        }
        
        private void PauseTimer() => runTimer = false;

        public void SetTimer(float time)
        {
            currentTimeRemaining = time;
            FormatTimer(currentTimeRemaining);
        }

        public void ResetTimer(float time) => currentTimeRemaining = time;

        
    }
}