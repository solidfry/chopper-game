﻿using UnityEngine;

namespace Weapons.ScriptableObjects
{
    [CreateAssetMenu(fileName = "_AmmoType", menuName = "Weapons/New Ammo", order = 0)]
    public class AmmoType : ScriptableObject
    {
        [SerializeField] private TrailRenderer trails;
        [SerializeField] private GameObject deathParticles;
        [SerializeField] private GameObject prefab;
        
        public TrailRenderer Trails => trails;
        public GameObject DeathParticles => deathParticles;
        public GameObject Prefab => prefab;


        public void InstantiateDeathParticles(Transform transform)
        {
            var particle = Instantiate(deathParticles, transform.position, Quaternion.identity);
            // if(particle.TryGetComponent(typeof(AmmoEffect), out var effect))
            // {
            //     effect.Trail = trails;
            // }
        }
    }
}