using Events;
using Interfaces;
using Unity.Collections;
using Unity.Jobs;
using Unity.Netcode;
using UnityEngine;
using Weapons.Jobs;
using Weapons.ScriptableObjects;

namespace Weapons
{
    public class AmmoEffectServer : NetworkBehaviour, IAmmo
    {
        [SerializeField] private AmmoType ammoType;
        [SerializeField] float maximumRange, distanceTraveled = 0;
        [SerializeField] private Vector3 previousPosition;
        [SerializeField] private Collider collider3D;
        [SerializeField] private GameObject graphics;
        private Vector3 _position;
        private bool _despawnHasBeenRequested = false;

        private int _damage;

        private void Awake()
        {
            _damage = ammoType.stats.Damage;
        }

        public override void OnNetworkSpawn()
        {
            if (collider3D == null)
                collider3D = GetComponent<Collider>();

            gameObject.layer = LayerMask.NameToLayer("Ammo");

            var tr = transform;
            previousPosition = tr.position;
            
            // Owner will not see the graphics from the server so that the firing feels responsive.
            if(IsOwner) 
               graphics.SetActive(false);
            
        }

        private void Update()
        {
            if (!IsServer) return;
            var dt = new NativeReference<float>(Allocator.TempJob);
            dt.Value = this.distanceTraveled;

            var position = transform.position;
            DistanceCalculationJob job = new DistanceCalculationJob
            {
                currentPosition = position,
                previousPosition = previousPosition,
                distanceTraveled = dt
            };

            var handle = job.Schedule();
            handle.Complete();

            this.distanceTraveled = dt.Value;
            dt.Dispose();

            previousPosition = position;

            if (this.distanceTraveled >= maximumRange)
                DoDestroy();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServer) return;

            IPlayer otherPlayer = collision.gameObject.GetComponentInParent<IPlayer>();
            
            if (otherPlayer != null && otherPlayer.PlayerOwnerNetworkId == OwnerClientId)
            {
                Debug.Log("Collision was ignored");
                Physics.IgnoreCollision(this.collider3D, collision.collider);
                return;
            }
            
            if(otherPlayer != null && otherPlayer.PlayerOwnerNetworkId != OwnerClientId)
            {
                DoDamage(collision, otherPlayer);
            }
            else
            {
                if (!_despawnHasBeenRequested)
                    DoDestroy();
            }
        }
        
        private void DoDamage(Collision collision, IPlayer otherPlayer)
        {
            if(!IsServer) return;
            
            // Debug.Log("DO Damage was run on server");
           
            if (otherPlayer.PlayerNetworkHealth.OwnerClientId == OwnerClientId)
            {
                Debug.Log("Bullet hit self so returned");
                return;
            }
            
            if (!_despawnHasBeenRequested)
                DoDestroy();
            
            otherPlayer.PlayerNetworkHealth.TakeDamage(_damage, OwnerClientId);
        }

        void DoDestroy()
        {
            _despawnHasBeenRequested = true;
            
            if (IsServer)
            {
                var particles = ammoType.InstantiateServerDeathParticles(transform);
                particles.Spawn();
                
                if(IsSpawned)
                    NetworkObject.Despawn();
                else 
                    Destroy(gameObject);
            }
        }
        
        public void SetAmmoType(AmmoType ammoTypeToSet) => this.ammoType = ammoTypeToSet;
        public void SetMaxRange(float maxRange) => maximumRange = maxRange;
        public AmmoType GetAmmoType() => ammoType;
    }
    
}