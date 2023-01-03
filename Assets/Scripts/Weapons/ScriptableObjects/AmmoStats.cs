using System;
using UnityEngine;

namespace Weapons.ScriptableObjects
{
    [Serializable]
    public struct AmmoStats
    {
        [SerializeField] private float explosionRadius;
        
        public float ExplosionRadius => explosionRadius;
    }
}