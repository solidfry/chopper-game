using System.Collections;
using UnityEngine;

namespace Weapons
{
    public class AmmoDestroyParticleHandler : MonoBehaviour
    {
        [SerializeField] ParticleSystem particles;
        private float _particleDuration;
        Coroutine _destroyCoroutine;
        IEnumerator _destroy;
    
        void Start()
        {
            if(particles == null)
                particles = GetComponent<ParticleSystem>();
        
            _destroy = DestroyAfterParticles(particles.main.duration);
            _particleDuration = particles.main.duration;
            _destroyCoroutine = StartCoroutine(_destroy);
        }

        private void OnDestroy()
        {
            if(_destroyCoroutine != null)
                StopCoroutine(_destroyCoroutine);
        }

        private IEnumerator DestroyAfterParticles(float duration)
        {
            yield return new WaitForSeconds(duration);
            Destroy(gameObject);
        }
    }
}
