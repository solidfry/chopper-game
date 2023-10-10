using System.Collections.Generic;
using Effects.Structs;
using Unity.Netcode;
using UnityEngine;

namespace Weapons.ScriptableObjects
{
    [CreateAssetMenu(fileName = "_WeaponType", menuName = "Weapons/New Weapon", order = 0)]
    public class WeaponType : ScriptableObject
    {
        [SerializeField] private AmmoType ammoType;
        [SerializeField] private WeaponStats stats;
        [SerializeField] private Weapon weaponPrefab;
        [SerializeField] public CameraShakeEvent shakeEvent;
        [SerializeField] private GameObject weaponMesh;
        [SerializeField] List<AudioClip> weaponFireClips; // Need to use these and remove any extra audio sources on the ammo prefab

        public AmmoType AmmoType
        {
            get => ammoType;
            private set => ammoType = value;
        }
        public WeaponStats Stats
        {
            get => stats;
            private set => stats = value;
        }

        public Weapon WeaponPrefab
        {
            get => weaponPrefab;
            private set => weaponPrefab = value;
        }

        public GameObject WeaponMesh
        {
            get => weaponMesh;
            private set => weaponMesh = value;
        }

        public void Fire(Transform firePoint)
        {
            Debug.Log($"Firing weapon {name}");
            AmmoEffect projectile = AmmoType.InstantiateAmmo(firePoint, Stats.RangeInMetres);
            var projectileRb = projectile.GetComponent<Rigidbody>();
            projectileRb.interpolation = RigidbodyInterpolation.Interpolate;
            projectileRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (projectile.TryGetComponent(out NetworkObject no))
                no.Spawn();
            else
            {
                projectile.SetNetworkObject();
                projectile.ProjectileNetworkObject.Spawn();
            }
            
            
            projectileRb.velocity = firePoint.transform.forward * Stats.ProjectileSpeed;
        }
        
        AudioClip GetRandomFireClip() => weaponFireClips[Random.Range(0, weaponFireClips.Count)];
        
        public Weapon InstantiateWeapon(Transform weaponPosition) => Instantiate(weaponPrefab, weaponPosition.position, Quaternion.identity, weaponPosition);
    }
}