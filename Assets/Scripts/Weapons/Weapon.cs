using System.Collections;
using Interfaces;
using Unity.Netcode;
using UnityEngine;
using Weapons.ScriptableObjects;

namespace Weapons
{
    public class Weapon : NetworkBehaviour, IFireWeapon
    {
        [SerializeField] private WeaponType weaponType;
        [SerializeField] private Transform firePointTr;
        [SerializeField] private GameObject prefabLocation;
        [SerializeField] private GameObject weaponModel;
        [SerializeField] private bool isFiring;
        
        Vector3 _firePointPosition;
        Quaternion _firePointRotation;
        
        [SerializeField] private WeaponStats stats;
        [SerializeField] private AmmoType ammoType;
        
        private float _firingCooldown;
        private float _firingCooldownTimer;
        
        IEnumerator _firingCoroutine;
        
        public bool IsFiring
        {
            get => isFiring; 
            set => isFiring = value;
        }
        
        private void Start()
        {
            if(weaponType == null) return;
            stats = weaponType.Stats;
            ammoType = weaponType.AmmoType;
            if (weaponType && weaponModel == null) weaponModel = Instantiate(weaponType.WeaponMesh, prefabLocation.transform);
            _firingCooldown = weaponType.Stats.FireRateInSeconds;
            _firingCoroutine = Firing(weaponType.Stats.FireRateInSeconds);
            _firingCooldownTimer = _firingCooldown;
        }
        private void OnDisable() => StopAllCoroutines();
        
        private void Update() => _firingCooldownTimer -= Time.deltaTime;
        
        
        [ContextMenu("Fire Attack")]
        public void DoAttack() 
        {
            // Check if the firing cooldown timer is greater than or equal to the firing cooldown
            if (_firingCooldownTimer <= 0)
            {
                // If the conditions are met, start firing and reset the firing cooldown timer
                IsFiring = true;
                StartCoroutine(_firingCoroutine);
            }
        }
        
        public void StopAttack()
        {
            IsFiring = false;
            StopCoroutine(_firingCoroutine); 
        }
        
        public void Fire(Vector3 position, Quaternion rotation)
        {
            AmmoEffect projectile = weaponType.InstantiateAmmoFromWeapon(position, rotation, out Rigidbody projectileRb);
            var forward = rotation * Vector3.forward;
            projectileRb.velocity = weaponType.SetProjectileVelocity(forward, stats.ProjectileSpeed);
            HandleNetworkObject(projectile);
        }

        private void HandleNetworkObject(AmmoEffect projectile)
        {
            if (!IsServer) return;

            if (projectile.TryGetComponent(out NetworkObject no))
            {
                no.Spawn();
                Debug.Log($"{ammoType.name} Projectile spawned");
            }
            else
            {
                Debug.Log("Else Proj spawned");

                projectile.ProjectileNetworkObject.Spawn();
            }
        }

        IEnumerator Firing(float fireRate)
        {
            while (IsFiring)
            {
                _firePointPosition = firePointTr.position; 
                _firePointRotation = firePointTr.rotation;
                weaponType.shakeEvent.Invoke();
                Fire(_firePointPosition, _firePointRotation);
                _firingCooldownTimer = _firingCooldown;
                yield return new WaitForSeconds(fireRate);
            }
        }
    }
}