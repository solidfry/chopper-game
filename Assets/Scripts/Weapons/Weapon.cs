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
        public AmmoType ammoType;
        [SerializeField] private Transform firePointTr;
        [SerializeField] private GameObject prefabLocation;
        [SerializeField] private GameObject weaponModel;
        [SerializeField] private bool isFiring;

        public WeaponStats Stats;
        
        Vector3 _firePointPosition;
        Quaternion _firePointRotation;

        private float _firingCooldown;
        private float _firingCooldownTimer;
        
        IEnumerator _firingCoroutine;
        
        public bool IsFiring
        {
            get => isFiring; 
            set => isFiring = value;
        }
        
        public override void OnNetworkSpawn()
        {
            // SpawnNetworkObjectIfApplicable();
            InitialiseFiringMechanics();
            InitialiseWeaponObject();
        }

        private void SpawnNetworkObjectIfApplicable()
        {
            if (IsServer)
                if(!IsSpawned)
                    NetworkObject.Spawn();
        }
        
        public void SpawnNetworkObjectOwnerViaOwnerClientID(ulong ownerClientId)
        {
            if(IsSpawned && ownerClientId != OwnerClientId)
            {
                NetworkObject.ChangeOwnership(ownerClientId);
                Debug.Log("Changed ownership of weapon");
            }
            else
            {
                NetworkObject.SpawnWithOwnership(ownerClientId);
                Debug.Log("Spawned with ownership of weapon");
            }
        }
        
        private void InitialiseWeaponObject()
        {
            if(WeaponType == null) return;
            if (weaponModel == null) weaponModel = Instantiate(WeaponType.WeaponMesh, prefabLocation.transform);
        }

        private void InitialiseFiringMechanics()
        {
            _firingCooldown = Stats.FireRateInSeconds;
            _firingCoroutine = Firing(Stats.FireRateInSeconds);
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
            
            if(IsServer)
            {
                HandleNetworkObject(projectile);
                projectileRb.velocity = WeaponType.SetProjectileVelocity(forward, WeaponType.Stats.ProjectileSpeed);
            }
            
            // if(IsClient && IsOwner || IsServer)
                projectileRb.velocity = WeaponType.SetProjectileVelocity(forward, WeaponType.Stats.ProjectileSpeed);
        }

        IEnumerator Firing(float fireRate)
        {
            while (IsFiring)
            {
                _firePointPosition = firePointTr.position; 
                _firePointRotation = firePointTr.rotation;
                WeaponType.shakeEvent.Invoke();
                
                if(IsClient && IsOwner)
                    FireServerRpc(_firePointPosition, _firePointRotation);
                
                if(IsServer)
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
        
        public void SetAmmoType(AmmoType ammoTypeToSet) => ammoType = ammoTypeToSet; 
        public void SetWeaponType(WeaponType weaponTypeToSet) => WeaponType = weaponTypeToSet;

        public override void OnDestroy()
        {
            StopAttack();
            base.OnDestroy();
        }
        
        [ServerRpc] 
        void FireServerRpc(Vector3 position, Quaternion rotation) => Fire(position, rotation);

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