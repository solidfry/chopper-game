using System;
using System.Collections;
using System.Collections.Generic;
using Interfaces;
using UnityEngine;

namespace Weapons
{
    public class WeaponSlot : MonoBehaviour, IAttackable
    {
        public Weapon weaponGameObjectInstance;
        IEnumerator _firingCoroutine;
        public event Action<WeaponSlot, Vector3, Quaternion> OnAttack;

        private void Start()
        {
            Debug.Log($"Fire rate is {weaponGameObjectInstance.stats.FireRateInSeconds}");
            _firingCoroutine = Firing(weaponGameObjectInstance.stats.FireRateInSeconds);
        }

        public void DoAttack() 
        {
            weaponGameObjectInstance.IsFiring = true;
            StartCoroutine(_firingCoroutine);
        }
        
        public void StopAttack()
        {
            weaponGameObjectInstance.IsFiring = false;
            if(_firingCoroutine != null)
                StopCoroutine(_firingCoroutine); 
        }
        
        public AmmoEffect Fire(Vector3 position, Quaternion rotation)
        {
            AmmoEffect projectile = weaponGameObjectInstance.weaponType.InstantiateAmmoFromWeapon(position, rotation);
            
            projectile.SetAmmoType(weaponGameObjectInstance.ammoType);
            projectile.SetMaxRange(weaponGameObjectInstance.stats.RangeInMetres);
            return projectile;
        }
        
        IEnumerator Firing(float fireRate)
        {
            while (weaponGameObjectInstance.IsFiring)
            {
                if (weaponGameObjectInstance.firingCooldownTimer <= 0)
                {
                    weaponGameObjectInstance.weaponType.shakeEvent.Invoke();
                    weaponGameObjectInstance.firingCooldownTimer = weaponGameObjectInstance.firingCooldown;
                    OnAttack?.Invoke(this, weaponGameObjectInstance.firePointPosition, transform.rotation);

                    yield return new WaitForSeconds(fireRate); // Maintains the fire rate
                }
               
            }

        }
    }
}