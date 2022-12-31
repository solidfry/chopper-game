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

        private void Awake()
        {
            if(collider3D != null)
                collider3D = GetComponent<Collider>();
        }

        private void Start()
        {
            previousPosition = transform.position;
        }

        private void FixedUpdate() => CheckDistanceTravelled();

        public void SetAmmoType(AmmoType ammoType)
        {
            this.ammoType = ammoType;
        }
        
        public void SetMaxRange(float maxRange) => maximumRange = maxRange;

        private void CheckDistanceTravelled()
        {
            distanceTraveled += Vector3.Distance(transform.position, previousPosition);
            previousPosition = transform.position;
            
            if(distanceTraveled >= maximumRange)
            {
                Destroy(gameObject);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            DestructionEffect();
        }

        void DestructionEffect()
        {
            //Instantiate some sort of particle effect and a trigger area for damage and physics impacts
            Destroy(gameObject);
        }
    }
}