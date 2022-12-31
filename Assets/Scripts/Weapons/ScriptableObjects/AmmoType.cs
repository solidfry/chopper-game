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

        public GameObject InstantiateDeathParticles(Transform transform)
        {
            return Instantiate(deathParticles, transform.position, Quaternion.identity);
        }
    }
}