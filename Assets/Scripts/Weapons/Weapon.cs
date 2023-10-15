using System.Collections;
using Interfaces;
using Unity.Netcode;
using UnityEngine;
using Weapons.ScriptableObjects;

namespace Weapons
{
    public class Weapon : NetworkBehaviour, IFireWeapon
    {
        [field: SerializeField] public WeaponType WeaponType { get; private set; }
        [field: SerializeField] public AmmoType AmmoType { get; private set; }
        [SerializeField] private Transform firePointTr;
        [SerializeField] private GameObject prefabLocation;
        [SerializeField] private GameObject weaponModel;
        [SerializeField] private bool isFiring;
        
        Vector3 _firePointPosition;
        Quaternion _firePointRotation;
        
        [SerializeField] private WeaponStats stats;

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
            SpawnNetworkObjectIfApplicable();
            InitialiseWeaponProperties();
            InitialiseWeaponObject();
            InitialiseFiringMechanics();
        }

        private void SpawnNetworkObjectIfApplicable()
        {
            if (IsServer || IsClient && IsOwner)
                if(IsSpawned == false)
                    NetworkObject.Spawn();
        }

        private void InitialiseWeaponProperties()
        {
            if(WeaponType == null) return;
            stats = WeaponType.Stats;
            SetAmmoType(WeaponType.AmmoType);
        }

        private void InitialiseWeaponObject()
        {
            if (WeaponType && weaponModel == null) weaponModel = Instantiate(WeaponType.WeaponMesh, prefabLocation.transform);
        }

        private void InitialiseFiringMechanics()
        {
            _firingCooldown = WeaponType.Stats.FireRateInSeconds;
            _firingCoroutine = Firing(WeaponType.Stats.FireRateInSeconds);
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
            AmmoEffect projectile = WeaponType.InstantiateAmmoFromWeapon(position, rotation, out Rigidbody projectileRb);
            var forward = rotation * Vector3.forward;
            if(IsClient && IsOwner || IsServer)
                projectileRb.velocity = WeaponType.SetProjectileVelocity(forward, stats.ProjectileSpeed);
            HandleNetworkObject(projectile);
        }

        IEnumerator Firing(float fireRate)
        {
            while (IsFiring)
            {
                _firePointPosition = firePointTr.position; 
                _firePointRotation = firePointTr.rotation;
                WeaponType.shakeEvent.Invoke();
                Fire(_firePointPosition, _firePointRotation);
                _firingCooldownTimer = _firingCooldown;
                yield return new WaitForSeconds(fireRate);
            }
        }
        
        private void HandleNetworkObject(AmmoEffect projectile)
        {
            if (!IsServer) return;
            
            if (projectile.TryGetComponent(out NetworkObject no)) no.Spawn();
        }
        
        public void SetAmmoType (AmmoType ammoTypeToSet) => AmmoType = ammoTypeToSet; 
        public void SetWeaponType(WeaponType weaponTypeToSet) => WeaponType = weaponTypeToSet;
        
        #if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (WeaponType == null) return;

            float offset = WeaponType.FirePointOffset;  // Make sure to expose FirePointOffset with a public getter in your ScriptableObject
            Vector3 firePoint = transform.position + transform.forward * offset;

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(firePoint, 0.2f);  // Draw a small red sphere at the fire point
        }
        #endif
    }
}