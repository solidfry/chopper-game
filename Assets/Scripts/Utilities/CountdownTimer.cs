using System;
using UnityEngine;

namespace Utilities
{
    [Serializable]
    public class CountdownTimer
    {
        [SerializeField] private float currentTimeRemaining;
        [SerializeField] private float countdownTime;
        [SerializeField] private bool runTimer;
        public float CurrentTimeRemaining => currentTimeRemaining;

        private float deltaTime;

        public CountdownTimer(float countdownTime, float deltaTime)
        {
            this.countdownTime = countdownTime;
            this.deltaTime = deltaTime;
            currentTimeRemaining = countdownTime;
        }
        
        public void OnUpdate()
        {
            if (!runTimer) return;
            if (currentTimeRemaining > 0)
            {
                currentTimeRemaining -= deltaTime;
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
    }
}