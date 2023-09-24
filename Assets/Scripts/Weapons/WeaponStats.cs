using System;
using UnityEngine;

namespace Weapons
{
    [Serializable]
    public struct WeaponStats
    {
        [SerializeField] private float fireRateInSeconds;
        [SerializeField] private float rangeInMetres;
        [SerializeField] private float spread;
        [SerializeField] private float reloadTimeInSeconds;
        [SerializeField] private float projectileSpeed;
        [SerializeField] private int magazineSize;
        [SerializeField] private int maxAmmo;
        
        public float FireRateInSeconds => fireRateInSeconds;
        public float RangeInMetres => rangeInMetres; 
        public float Spread => spread;
        public float ReloadTimeInSeconds => reloadTimeInSeconds;
        public int MagazineSize => magazineSize;
        public int MaxAmmo => maxAmmo;
        public float ProjectileSpeed => projectileSpeed;
    }
}