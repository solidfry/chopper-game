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
        [SerializeField] private WeaponType weaponType;
        [SerializeField] private Weapon weaponGameObject;
        
        public Transform Transform => transform;
        public WeaponType WeaponType => weaponType;

        public Weapon WeaponGameObject
        {
            get => weaponGameObject; 
            set => weaponGameObject = value;
        }

        public void DoAttack() => WeaponGameObject.DoAttack();

        public void StopAttack() => WeaponGameObject.StopAttack();

    }
}