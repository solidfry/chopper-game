using Interfaces;
using UnityEngine;
using UnityEngine.Serialization;

namespace Weapons.ScriptableObjects
{
    [CreateAssetMenu(fileName = "_AmmoType", menuName = "Weapons/New Ammo", order = 0)]
    public class AmmoType : ScriptableObject
    {
        public AmmoStats stats;
        
        [SerializeField] private TrailRenderer trails;
        [SerializeField] private GameObject deathParticles;
        [SerializeField] private AmmoEffect ammoBasePrefab;
        [SerializeField] private GameObject graphicsPrefab;
        
        public TrailRenderer Trails => trails;
        public GameObject DeathParticles => deathParticles;
        public GameObject GraphicsPrefab => graphicsPrefab;

        public AmmoEffect InstantiateAmmo(Vector3 position, Quaternion rotation)
        {
            AmmoEffect ammo = Instantiate(ammoBasePrefab, position, rotation);
            return ammo;
        }
        
        // public GameObject InstantiateGraphicsPrefab(Transform parent) => Instantiate(graphicsPrefab, parent.position, parent.rotation, parent);
        // public GameObject GetGraphicsPrefab() => graphicsPrefab;
        public AmmoEffect GetAmmoEffectPrefab() => ammoBasePrefab;
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