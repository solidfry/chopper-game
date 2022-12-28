using System.Collections;
using Interfaces;
using UnityEngine;

namespace Weapons
{
    public class Weapon : MonoBehaviour, IFireWeapon
    {
        [SerializeField] private WeaponType weaponType;
        [SerializeField] private Transform firePoint;
        [SerializeField] private GameObject prefabLocation;
        [SerializeField] private GameObject weaponModel;
        
        private WeaponStats stats;

        public bool isFiring;
        
        private void Awake()
        {
            if(weaponType == null) return;
            
            stats = weaponType.Stats;
            if (weaponType && weaponModel == null) weaponModel = Instantiate(weaponType.WeaponMesh, prefabLocation.transform);
        }

        public void DoAttack() => Fire(firePoint, stats.ProjectileSpeed, stats.Damage, stats.RangeInMetres);

        public void Fire(Transform firePoint, float speed, float damage, float range, float spread = 0f)
        {
            Debug.Log("Firing weapon");
        }

        IEnumerator Firing()
        {
            while (isFiring)
            {
                // Fire();
                yield return new WaitForSeconds(weaponType.Stats.FireRateInSeconds);
            }
        }
        
    }
}