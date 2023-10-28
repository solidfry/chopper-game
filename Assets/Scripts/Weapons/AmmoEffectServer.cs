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
        [SerializeField] private GameObject graphics;
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
            
            // Owner will not see the graphics from the server so that the firing feels responsive.
            if(IsOwner) 
               graphics.SetActive(false); 
            
        }

        private void Update()
        {
            if (!IsServer) return;
            CheckDistanceTravelled();
        }

        private void CheckDistanceTravelled()
        {
            _position = transform.position;
            distanceTraveled += Vector3.Distance(_position, previousPosition);
            previousPosition = _position;

            if (distanceTraveled >= maximumRange && !_despawnHasBeenRequested)
                DoDestroy();
        }

        private void OnCollisionEnter(Collision collision)
        {
            
            if (!IsServer) return;

            DoDamage(collision);

            if (!_despawnHasBeenRequested)
                DoDestroy();
        }
        
        private void OnTriggerEnter(Collider collision)
        {
            if (!IsServer) return;

            DoDamageTrigger(collision);

            if (!_despawnHasBeenRequested)
                DoDestroy();
        }
        
        private void DoDamage(Collision collision)
        {
            if(!IsServer) return;
            
            if (collision.collider.TryGetComponent(out IPlayer player))
            {
                if(player.PlayerNetworkID == ProjectileNetworkObject.OwnerClientId) return;
                player.Health.TakeDamage(ammoType.stats.Damage);
                Debug.Log("damage taken from collider " + ammoType.stats.Damage);
            }
        
            Debug.Log(collision.collider.name);
            
        }
        
        private void DoDamageTrigger(Collider collision)
        {
            if(!IsServer) return;
            
            if (collision.TryGetComponent(out IPlayer player))
            {
                if(player.PlayerNetworkID == ProjectileNetworkObject.OwnerClientId) return;
                player.Health.TakeDamage(ammoType.stats.Damage);
                Debug.Log("damage taken from collider " + ammoType.stats.Damage);
            }
            
            Debug.Log(collision.name + " from trigger");
        
        
        }

        void DoDestroy()
        {
            _despawnHasBeenRequested = true;
            
            if (IsServer)
            {
                var particles = ammoType.InstantiateServerDeathParticles(transform);
                particles.Spawn();
                if (!ProjectileNetworkObject.IsSpawned) return;
                
                ProjectileNetworkObject.Despawn();
            }
        }
        
        public void SetAmmoType(AmmoType ammoTypeToSet) => this.ammoType = ammoTypeToSet;
        public void SetMaxRange(float maxRange) => maximumRange = maxRange;
        public AmmoType GetAmmoType() => ammoType;
    }
    
}