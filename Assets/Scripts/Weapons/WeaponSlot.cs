using System;
using Interfaces;
using UnityEngine;
using Weapons.ScriptableObjects;

namespace Weapons
{
    [Serializable]
    public class WeaponSlot : IAttackable
    {
        [SerializeField] private Transform transform;
        [SerializeField] private WeaponType data;
        private Weapon _weaponGameObject;
        
        public Transform Transform => transform;
        public WeaponType Data => data;

        public Weapon WeaponGameObject
        {
            get => _weaponGameObject; 
            set => _weaponGameObject = value;
        }

        public void DoAttack() => WeaponGameObject.DoAttack();

        public void StopAttack() => WeaponGameObject.StopAttack();

        public void InstantiateAttackable(Transform tr, Transform parent) => Data.InstantiateWeapon(tr, parent);
    }
}