using Interfaces;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Weapons.Jobs;
using Weapons.ScriptableObjects;

namespace Weapons
{
    public class AmmoEffectClient : MonoBehaviour, IAmmo
    {
        [SerializeField] private AmmoType ammoType;
        [SerializeField] float maximumRange, distanceTraveled = 0;
        [SerializeField] private Vector3 previousPosition;
        [SerializeField] private Collider collider3D;
        [SerializeField] private LayerMask ignoreCollisionsOnLayer;
        public Rigidbody Rigidbody { get; set; }
        private Vector3 _position;

        private void Awake() => Rigidbody = GetComponent<Rigidbody>();

        public void Start()
        {
            if (collider3D == null)
                collider3D = GetComponent<Collider>();

            gameObject.layer = LayerMask.NameToLayer("Ammo");
            

            var tr = transform;
            previousPosition = tr.position;
        }

        private void Update()
        {
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
                DestructionEffect();
        }

        public void SetAmmoType(AmmoType ammoTypeToSet) => this.ammoType = ammoTypeToSet;

        public void SetMaxRange(float maxRange) => maximumRange = maxRange;

        private void OnCollisionEnter(Collision collision)
        {
            // bitwise operation to compare layer and layermask variable and do physics.ignorecollision if true
            if ((ignoreCollisionsOnLayer.value & (1 << collision.gameObject.layer)) != 0)
            {
                Physics.IgnoreCollision(collider3D, collision.collider);
                return;
            }
            
            DestructionEffect();
        }

        void DestructionEffect()
        {
            ammoType.InstantiateDeathParticles(transform);
            Destroy(gameObject);
        }
        
        public AmmoType GetAmmoType() => ammoType;
    }
}