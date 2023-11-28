using System;
using UnityEngine;

namespace Utilities
{
    [Serializable]
    public class CountdownTimer
    {
        [SerializeField] private float currentTimeRemaining;
        [SerializeField] private bool runTimer;
        public float CurrentTimeRemaining => currentTimeRemaining;

        private float _deltaTime;

        public CountdownTimer(float countdownTime, float deltaTime)
        {
            _deltaTime = deltaTime;
            currentTimeRemaining = countdownTime;
        }
        
        public void OnUpdate()
        {
            if (!runTimer) return;
            if (currentTimeRemaining > 0)
            {
                currentTimeRemaining -= _deltaTime;
            }
            else
            {
                currentTimeRemaining = 0;
            }
        }
        
        public void StartTimer()
        {
            runTimer = true;
        }
        
        public void StopTimer()
        {
            runTimer = false;
        }
        
        public void ResetTimer(float time)
        {
            currentTimeRemaining = time;
        }
        
        public bool GetTimerStatus => runTimer;
    }
}