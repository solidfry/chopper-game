using System;

namespace Interfaces
{
    public interface IDeathEffect
    {
        [Serializable]
        public enum DeathEffectType
        {
            None,
            Particle,
            Animation,
            Sound,
            ExplosiveForce,
        }
        
        public void DoDeathEffect();
    }
}