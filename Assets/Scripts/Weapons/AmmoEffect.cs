using Interfaces;
using Unity.Netcode;
using UnityEngine;
using Weapons.ScriptableObjects;

namespace Weapons
{
    public class AmmoEffect : NetworkBehaviour
    {
        [SerializeField] private AmmoType ammoType;
        [SerializeField] float maximumRange, distanceTraveled = 0;
        [SerializeField] private Vector3 previousPosition;
        [SerializeField] private Collider collider3D;
        [SerializeField] private LayerMask ignoreCollisionsOnLayer;
        [field: SerializeField] public NetworkObject ProjectileNetworkObject { get; private set; }
        private Vector3 _position;

        public override void OnNetworkSpawn()
        {
            if (collider3D == null)
                collider3D = GetComponent<Collider>();
            
            gameObject.layer = LayerMask.NameToLayer("Ammo");
            
            if(ProjectileNetworkObject == null)
                ProjectileNetworkObject = GetComponent<NetworkObject>();
            
            previousPosition = transform.position;
        }

        private void FixedUpdate() => CheckDistanceTravelled();

        public void SetAmmoType(AmmoType ammoTypeToSet) => this.ammoType = ammoTypeToSet;

        public void SetMaxRange(float maxRange) => maximumRange = maxRange;

        private void CheckDistanceTravelled()
        {
            _position = transform.position;
            distanceTraveled += Vector3.Distance(_position, previousPosition);
            previousPosition = _position;

            if (distanceTraveled >= maximumRange)
            {
                ProjectileNetworkObject.Despawn();
                Destroy(gameObject); // Pool this object instead
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            var target = collision.collider.GetComponent<IDamageable>();
            if (target != null)
            {
                target.TakeDamage(ammoType.stats.Damage);
            }
            DestructionEffect();
        }

        void DestructionEffect() 
        {
            Debug.Log("Destruction effect");
            ProjectileNetworkObject.Despawn();
            Destroy(gameObject);
        }
        
        public void SetNetworkObject()
        {
            if (ProjectileNetworkObject == null && TryGetComponent(out NetworkObject no))
                ProjectileNetworkObject = no;
            else
                ProjectileNetworkObject = gameObject.AddComponent<NetworkObject>();
        }
    }
}