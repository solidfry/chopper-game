using Interfaces;
using UnityEngine;
using Weapons.ScriptableObjects;

namespace Weapons
{
    public class AmmoEffect : MonoBehaviour
    {
        [SerializeField] private AmmoType ammoType;
        [SerializeField] float maximumRange, distanceTraveled = 0;
        [SerializeField] private Vector3 previousPosition;
        [SerializeField] private Collider collider3D;
        [SerializeField] private LayerMask ignoreCollisionsOnLayer;

        private void Awake()
        {
            if (collider3D != null)
                collider3D = GetComponent<Collider>();

            ignoreCollisionsOnLayer = LayerMask.NameToLayer("Ignore Player");
            gameObject.layer = ignoreCollisionsOnLayer;
        }

        private void Start()
        {
            previousPosition = transform.position;
        }

        private void FixedUpdate() => CheckDistanceTravelled();

        public void SetAmmoType(AmmoType ammoTypeToSet) => this.ammoType = ammoTypeToSet;

        public void SetMaxRange(float maxRange) => maximumRange = maxRange;

        private void CheckDistanceTravelled()
        {
            var position = transform.position;
            distanceTraveled += Vector3.Distance(position, previousPosition);
            previousPosition = position;

            if (distanceTraveled >= maximumRange)
                Destroy(gameObject); // Pool this object instead

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
            Destroy(gameObject);
        }
            //Instantiate some sort of particle effect and a trigger area for damage and physics impacts
            
    }
}