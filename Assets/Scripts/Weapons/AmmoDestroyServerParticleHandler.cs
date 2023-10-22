using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Weapons
{
    public class AmmoDestroyServerParticleHandler : NetworkBehaviour
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

        public override void OnDestroy()
        {
            if(_destroyCoroutine != null)
                StopCoroutine(_destroyCoroutine);
            base.OnDestroy();
        }

        private IEnumerator DestroyAfterParticles(float duration)
        {
            yield return new WaitForSeconds(duration);
            this.NetworkObject.Despawn();
        }
    }
}
