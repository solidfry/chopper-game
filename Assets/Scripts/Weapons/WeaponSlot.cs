using System;
using Interfaces;
using UnityEngine;
using Weapons.ScriptableObjects;

namespace Weapons
{
    [Serializable]
    public class WeaponSlot
    {
        [SerializeField] private Transform transform;
        [SerializeField] private WeaponType data;
        private IFireWeapon weaponGameObject;
        
        public Transform Transform => transform;
        public WeaponType Data => data;

        public IFireWeapon WeaponGameObject
        {
            get => weaponGameObject; 
            set => weaponGameObject = value;
        }

    }
}