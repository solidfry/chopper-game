using System;
using UnityEngine;
using Weapons.ScriptableObjects;

namespace Weapons
{
    [Serializable]
    public class WeaponSlot
    {
        [SerializeField] private Transform transform;
        [SerializeField] private WeaponType data;
        private Weapon weaponGameObject;
        
        public Transform Transform { get => transform; }
        public WeaponType Data { get => data; }

        public Weapon WeaponGameObject
        {
            get => weaponGameObject; 
            set => weaponGameObject = value;
        }

    }
}