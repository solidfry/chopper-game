using System.Collections;
using Interfaces;
using Unity.Netcode;
using UnityEngine;
using Weapons.ScriptableObjects;

namespace Weapons
{
    public class Weapon : MonoBehaviour, IFireWeapon
    {
        [SerializeField] private WeaponType weaponType;
        [SerializeField] private Transform firePointTr;
        [SerializeField] private GameObject prefabLocation;
        [SerializeField] private GameObject weaponModel;
        [SerializeField] private bool isFiring;
        
        [SerializeField] private WeaponStats stats;
        
        private float firingCooldown;
        private float firingCooldownTimer;
        
        IEnumerator firingCoroutine;
        
        public bool IsFiring
        {
            get => isFiring; 
            set => isFiring = value;
        }
        
        private void Start()
        {
            if(weaponType == null) return;
            stats = weaponType.Stats;
            if (weaponType && weaponModel == null) weaponModel = Instantiate(weaponType.WeaponMesh, prefabLocation.transform);
            firingCooldown = weaponType.Stats.FireRateInSeconds;
            firingCoroutine = Firing(weaponType.Stats.FireRateInSeconds);
            firingCooldownTimer = firingCooldown;
        }
        private void OnDisable() => StopAllCoroutines();
        
        private void Update() => firingCooldownTimer -= Time.deltaTime;
        
        // private void Start() => firingCooldownTimer = firingCooldown;

        
        [ContextMenu("Fire Attack")]
        public void DoAttack() 
        {
            // Check if the firing cooldown timer is greater than or equal to the firing cooldown
            if (firingCooldownTimer <= 0)
            {
                // If the conditions are met, start firing and reset the firing cooldown timer
                IsFiring = true;
                StartCoroutine(firingCoroutine);
            }
        }
        
        public void StopAttack()
        {
            IsFiring = false;
            StopCoroutine(firingCoroutine); 
        }
        
        public void Fire(Transform firePoint)
        {
            firePoint= firePointTr;
            weaponType.Fire(firePoint);
        }

        IEnumerator Firing(float fireRate)
        {
            while (IsFiring)
            {
                weaponType.shakeEvent.Invoke();
                Fire(firePointTr);
                firingCooldownTimer = firingCooldown;
                yield return new WaitForSeconds(fireRate);
            }
        }
    }
}