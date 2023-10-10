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
        private IFireWeapon _weaponGameObject;
        
        public Transform Transform => transform;
        public WeaponType Data => data;

        public IFireWeapon WeaponGameObject
        {
            get => _weaponGameObject; 
            set => _weaponGameObject = value;
        }

    }
}