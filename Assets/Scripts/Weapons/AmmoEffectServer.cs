using Interfaces;
using Unity.Netcode;
using UnityEngine;
using Weapons.ScriptableObjects;

namespace Weapons
{
    public class AmmoEffectServer : NetworkBehaviour, IAmmo
    {
        [SerializeField] private AmmoType ammoType;
        [SerializeField] float maximumRange, distanceTraveled = 0;
        [SerializeField] private Vector3 previousPosition;
        [SerializeField] private Collider collider3D;
        [SerializeField] private LayerMask ignoreCollisionsOnLayer;
        [field: SerializeField] public NetworkObject ProjectileNetworkObject { get; private set; }
        public Rigidbody Rigidbody { get; set; }
        private Vector3 _position;
        private bool _despawnHasBeenRequested = false;

        private void Awake() => Rigidbody = GetComponent<Rigidbody>();

        public override void OnNetworkSpawn()
        {
            if (collider3D == null)
                collider3D = GetComponent<Collider>();

            gameObject.layer = LayerMask.NameToLayer("Ammo");

            if (ProjectileNetworkObject == null)
                ProjectileNetworkObject = GetComponent<NetworkObject>();

            var tr = transform;
            previousPosition = tr.position;
            
            if(IsOwner) 
                this.GetComponentInChildren<GameObject>().gameObject.SetActive(false); 
            
        }

        private void Update() => CheckDistanceTravelled();

        private void CheckDistanceTravelled()
        {
            if (!IsServer) return;

            _position = transform.position;
            distanceTraveled += Vector3.Distance(_position, previousPosition);
            previousPosition = _position;

            if (distanceTraveled >= maximumRange && !_despawnHasBeenRequested)
                DoDestroy();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServer) return;

            // Debug.Log($"collider3D with {collision.gameObject.name}");
            if (collision.collider.TryGetComponent(out IDamageable damageable))
                damageable.TakeDamage(ammoType.stats.Damage);

            if (!_despawnHasBeenRequested)
                DoDestroy();
        }

        private void OnTriggerEnter(Collider collision)
        {
            if (!IsServer) return;

            if (collision.TryGetComponent(out IDamageable damageable))
                damageable.TakeDamage(ammoType.stats.Damage);

            if (!_despawnHasBeenRequested)
                DoDestroy();
        }

        void DestructionEffect()
        {
            if (!ProjectileNetworkObject.IsSpawned) return;
            // Debug.Log("Destruction effect");
            if (IsServer)
            {
                ProjectileNetworkObject.Despawn();
            }
        }

        void DoDestroy()
        {
            _despawnHasBeenRequested = true;
            if (IsServer)
                DestructionEffect();
        }
        
        public void SetAmmoType(AmmoType ammoTypeToSet) => this.ammoType = ammoTypeToSet;

        public void SetMaxRange(float maxRange) => maximumRange = maxRange;
        public AmmoType GetAmmoType() => ammoType;

    }
    
}