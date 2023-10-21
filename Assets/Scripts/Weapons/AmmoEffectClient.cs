using Interfaces;
using UnityEngine;
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

        private void Update() => CheckDistanceTravelled();

        public void SetAmmoType(AmmoType ammoTypeToSet) => this.ammoType = ammoTypeToSet;

        public void SetMaxRange(float maxRange) => maximumRange = maxRange;

        private void CheckDistanceTravelled()
        {
            _position = transform.position;
            distanceTraveled += Vector3.Distance(_position, previousPosition);
            previousPosition = _position;

            if (distanceTraveled >= maximumRange)
                DestructionEffect();
        }

        private void OnCollisionEnter(Collision collision)
        {
            DestructionEffect();
        }

        void DestructionEffect()
        {
            Destroy(this.gameObject);
        }
        
        public AmmoType GetAmmoType() => ammoType;
    }
}