using System.Collections;
using Interfaces;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using Weapons.ScriptableObjects;

namespace Weapons
{
    public class Weapon : MonoBehaviour
    {
        public WeaponType weaponType;
        public AmmoType ammoType;
        public AmmoEffect ammoEffect;

        [SerializeField] public Transform firePointTr;

        // [SerializeField] private Transform parent;
        [SerializeField] private GameObject prefabLocation;
        [SerializeField] private GameObject weaponModel;
        [SerializeField] private bool isFiring;
        // [SerializeField] private ulong ownerId;

        public WeaponStats stats;

        public float firingCooldown;

        public float firingCooldownTimer;

        float _firePointOffset;

        public Vector3 firePointPosition;

        public bool IsFiring
        {
            get => isFiring;
            set => isFiring = value;
        }

        private void Awake()
        {
            ammoType = weaponType.AmmoType;
            // ammoEffect = ammoType.GetAmmoEffectPrefab();
            stats = weaponType.Stats;
            firingCooldown = stats.FireRateInSeconds;
            firingCooldownTimer = firingCooldown;
            firePointPosition = FirePoint();
        }

        public void Start()
        {
            firingCooldown = stats.FireRateInSeconds;
            firingCooldownTimer = firingCooldown;
        }

        private void OnDisable() => StopAllCoroutines();

        private void Update()
        {
            firePointPosition = FirePoint();
            firingCooldownTimer -= Time.deltaTime;
        }

        private Vector3 FirePoint()
        {
            _firePointOffset = weaponType.FirePointOffset;
            var tr = transform;
            return tr.position + tr.forward * _firePointOffset;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (weaponType == null) return;

            var firePoint = FirePoint();

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(firePoint, 0.2f); // Draw a small red sphere at the fire point
        }
#endif

       
    }
}