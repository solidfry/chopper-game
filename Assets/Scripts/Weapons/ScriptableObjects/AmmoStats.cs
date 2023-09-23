using System;
using UnityEngine;

namespace Weapons.ScriptableObjects
{
    [Serializable]
    public struct AmmoStats
    {
        [SerializeField] private float explosionRadius;
        [SerializeField] private int damage;
        public float ExplosionRadius => explosionRadius;
        public int Damage => damage;
    }
}