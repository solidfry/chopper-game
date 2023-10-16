using System;
using Interfaces;
using UnityEngine;
using UnityEngine.Serialization;
using Weapons.ScriptableObjects;

namespace Weapons
{
    [Serializable]
    public class WeaponSlot : IAttackable
    {
        [SerializeField] private Transform transform;
        [SerializeField] private WeaponType weaponType;
        private Weapon _weaponGameObject;
        
        public Transform Transform => transform;
        public WeaponType WeaponType => weaponType;

        public Weapon WeaponGameObject
        {
            get => _weaponGameObject; 
            set => _weaponGameObject = value;
        }

        public void DoAttack() => WeaponGameObject.DoAttack();

        public void StopAttack() => WeaponGameObject.StopAttack();

    }
}