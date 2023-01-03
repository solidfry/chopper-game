using Interfaces;
using UnityEngine;

namespace Weapons.ScriptableObjects
{
    [CreateAssetMenu(fileName = "_AmmoType", menuName = "Weapons/New Ammo", order = 0)]
    public class AmmoType : ScriptableObject
    {
        [SerializeField] private TrailRenderer trails;
        [SerializeField] private GameObject deathParticles;
        [SerializeField] private GameObject prefab;
        
        public TrailRenderer Trails => trails;
        public GameObject DeathParticles => deathParticles;
        public GameObject Prefab => prefab;

        public GameObject InstantiateAmmo(Transform transform, float rangeInMetres = 50f)
        {
            var ammo = Instantiate(prefab, transform.position, transform.rotation);
            var effect = ammo.AddComponent<AmmoEffect>();
            effect.SetAmmoType(this);
            effect.SetMaxRange(rangeInMetres);
            return ammo;
        }

        public GameObject InstantiateDeathParticles(Transform transform, float explosionRadius, int damage)
        {
            return Instantiate(deathParticles, transform.position, Quaternion.identity);
        }

        public void CreateRayCastExplosion(Transform transform, float explosionRadius, int damage)
        {
            if (explosionRadius <= 0) return;
            // var colliders = Physics.OverlapSphere(transform.position, explosionRadius);
            // Raycast sphere to find targets hit by the explosion
            var hits = Physics.SphereCastAll(transform.position, explosionRadius, Vector3.up, 0f);
            
            if (hits.Length == 0) return;
            
            foreach (var hit in hits)
            {
                var target = hit.collider.GetComponent<IDamageable>();
                if (target != null)
                {
                    target.TakeDamage(damage);
                }
            }
        }
    }
}