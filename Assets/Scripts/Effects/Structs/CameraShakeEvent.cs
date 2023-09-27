using System;
using Enums;
using Events;
using UnityEngine;

namespace Effects.Structs
{
    [Serializable]
    public struct CameraShakeEvent
    {
        [SerializeField] Strength strength;
        [SerializeField] float duration;
        [SerializeField] bool doShake;

        public CameraShakeEvent(Strength strength, float duration, bool doShake)
        {
            this.doShake = doShake;
            this.strength = strength;
            this.duration = duration;
        }
        
        public void Invoke()
        {
            if (!doShake) return;
            GameEvents.onScreenShakeEvent?.Invoke(strength, duration);
        }
    }
}