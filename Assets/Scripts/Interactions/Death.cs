using System;
using System.Collections.Generic;
using Interactions.ScriptableObjects;
using Interfaces;
using UnityEngine;

namespace Interactions
{
    [Serializable]
    public class Death 
    {
        [SerializeField] ParticleSystem particles;
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip audioClip;
        private Transform parent;
        [SerializeField] bool isExplosive;
        [SerializeField] List<DeathEffect> deathEffects;
    
        // An array of death effects that can be of type Particle, Audio, Animation, Apply Force Explosion, etc.
        // This is a good example of the Strategy Pattern.
        // https://en.wikipedia.org/wiki/Strategy_pattern
        // so i will create a based interface called IDeathEffect and then create a bunch of subclasses that inherit from it.
        // It should probably be a ScriptableObject so that it can be created in the editor and then assigned to the Death component.
    
        public void Init(Transform transform = null)
        {
            if(transform)
                parent = transform;
        
            audioSource.clip = audioClip;
        }

        public void Play()
        {
            if (particles.isPlaying || audioSource.isPlaying) return;
            PlayParticles();
            PlayAudio();

            ApplyForceExplosion();
            
        }

        private void PlayParticles()
        {
            if (particles != null)
            {
                ParticleSystem.Instantiate(particles, parent);
            }
        }

        private void PlayAudio()
        {
            if (audioClip != null && audioSource != null)
                audioSource.PlayOneShot(audioClip);
        }

        private void ApplyForceExplosion()
        {
            if (!isExplosive) return;

            var rb = parent.GetComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.AddForceAtPosition(Vector3.up * rb.mass * 1000, parent.position);
        }
        
        public void PlayEffects()
        {
            foreach (var effect in deathEffects)
            {
                effect.DoDeathEffect();
            }
        }
    }
}

