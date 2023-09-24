using System;
using Enums;
using Events;

namespace Structs
{
    [Serializable]
    public struct CameraShakeEvent
    {
        public Strength strength;
        public float duration;
        public bool doShake;

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