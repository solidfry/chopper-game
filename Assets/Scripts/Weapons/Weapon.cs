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
        public AmmoType AmmoType { get; private set; }
        public AmmoEffect AmmoEffect { get; private set; }
        [SerializeField] private Transform firePointTr;
        [SerializeField] private Transform parent;
        [SerializeField] private GameObject prefabLocation;
        [SerializeField] private GameObject weaponModel;
        [SerializeField] private bool isFiring;
        [SerializeField] private ulong ownerId;

        WeaponStats _stats;
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
            if (IsClient || IsLocalPlayer)
            {
                Debug.Log("Start ran");
                InitialiseFiringMechanics();
                InitialiseWeaponObject();
            }
            
        }
        
        private void InitialiseWeaponObject()
        {
            if (WeaponType != null)
            {
                Debug.Log("Weapon type was not null");
                AmmoType = WeaponType.AmmoType;
                AmmoEffect = AmmoType.GetAmmoEffectPrefab();
                weaponModel = Instantiate(WeaponType.WeaponMesh, prefabLocation.transform);
            }
            
        }

        private void InitialiseFiringMechanics()
        {
            _firingCooldown = _stats.FireRateInSeconds;
            _firingCoroutine = Firing(_stats.FireRateInSeconds);
            _firingCooldownTimer = _firingCooldown;
        }

        private void OnDisable() => StopAllCoroutines();
        
        private void Update() => _firingCooldownTimer -= Time.deltaTime;
        
        
        [ContextMenu("Fire Attack")]
        public void DoAttack() 
        {
            if(!IsOwner) return; 
            
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
            if(!IsOwner) return; 

            IsFiring = false;
            if(_firingCoroutine != null)
                StopCoroutine(_firingCoroutine); 
        }
        
        public void Fire(Vector3 position, Quaternion rotation)
        {
            AmmoEffect projectile = WeaponType.InstantiateAmmoFromWeapon(position, rotation);
            if(IsClient && IsOwner)
            {
                Instantiate(AmmoType.GetGraphicsPrefab(),
                    position,
                    rotation,
                    projectile.transform);
                projectile.SetAmmoType(AmmoType);
                projectile.SetMaxRange(_stats.RangeInMetres);
                Rigidbody projectileRb = projectile.GetComponent<Rigidbody>();
                var forward = rotation * Vector3.forward;
                projectileRb.interpolation = RigidbodyInterpolation.Interpolate;
                projectileRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                projectileRb.isKinematic = false;
                projectileRb.AddForce(forward * _stats.ProjectileSpeed, ForceMode.VelocityChange);
                projectileRb.excludeLayers = LayerMask.GetMask("Ammo", "Weapon", "LocalPlayer");
            }
            
            if(IsServer && !IsLocalPlayer)
                projectile.GetComponent<NetworkObject>().SpawnWithOwnership(OwnerClientId);
        }
        
        IEnumerator Firing(float fireRate)
        {
            while (IsFiring)
            {
                _firePointPosition = firePointTr.position; 
                _firePointRotation = firePointTr.rotation;
                WeaponType.shakeEvent.Invoke();
                
                if(IsClient && IsOwner)
                {
                    FireServerRpc(_firePointPosition, _firePointRotation);
                }
                else
                {
                    Fire(_firePointPosition, _firePointRotation);
                }
                
                _firingCooldownTimer = _firingCooldown;
                yield return new WaitForSeconds(fireRate);
            }
        }
        
        public void SetAmmoType(AmmoType ammoTypeToSet) => AmmoType = ammoTypeToSet; 
        public void SetStats(WeaponStats statsToSet) => _stats = statsToSet;
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