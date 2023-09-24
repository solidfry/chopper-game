using System;
using Events;
using UnityEngine;

namespace Structs
{
    [Serializable]
    public struct ParticleEvent
    {
        [field: SerializeField] public Transform Transform { get; set; }
        [field: SerializeField] public ParticleSystem Particle { get; private set; }
        [field: SerializeField] public bool DoParticles { get; private set; }
        [field: SerializeField] public Color Color { get; private set; }

        public void Invoke()
        {
            if (!DoParticles) return;

            GameEvents.onParticleEvent?.Invoke(this);
        }
    }
}