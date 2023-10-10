using Interfaces;
using UnityEngine;

namespace Weapons.ScriptableObjects
{
    [CreateAssetMenu(fileName = "_AmmoType", menuName = "Weapons/New Ammo", order = 0)]
    public class AmmoType : ScriptableObject
    {
        public AmmoStats stats;
        
        [SerializeField] private TrailRenderer trails;
        [SerializeField] private GameObject deathParticles;
        [SerializeField] private GameObject prefab;
        
        public TrailRenderer Trails => trails;
        public GameObject DeathParticles => deathParticles;
        public GameObject Prefab => prefab;

        public AmmoEffect InstantiateAmmo(Transform transform, float rangeInMetres = 50f)
        {
            var ammo = Instantiate(prefab, transform.position, transform.rotation);
            var effect = ammo.AddComponent<AmmoEffect>();
            effect.SetAmmoType(this);
            effect.SetMaxRange(rangeInMetres);
            return effect;
        }

        public GameObject InstantiateDeathParticles(Transform transform) => Instantiate(deathParticles, transform.position, Quaternion.identity);

        public void CreateRayCastExplosion(Transform transform, float explosionRadius, int damage)
        {
            if (explosionRadius == 0) return;
            var results = new RaycastHit[10];
            var size = Physics.SphereCastNonAlloc(transform.position, explosionRadius, Vector3.up, results, 0f);
            
            if (size == 0) return;
            foreach (var result in results)
                if(result.collider.TryGetComponent(out IDamageable damageable))
                    damageable.TakeDamage(damage);
        }
    }
}