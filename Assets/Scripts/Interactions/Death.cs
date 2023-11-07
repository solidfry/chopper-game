using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Events;
using Interactions.ScriptableObjects;
using Interfaces;
using PlayerInteraction.Networking;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Interactions
{
    [Serializable]
    public class Death 
    {
        [SerializeField] ParticleSystem particles;
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip audioClip;
        private Transform _parent;
        [field: SerializeField] public bool IsDead { get; private set; }
        [SerializeField] bool isExplosive;
        float ClipLength => audioClip.length;
        
        // [SerializeField] List<DeathEffect> deathEffects;
    
        // An array of death effects that can be of type Particle, Audio, Animation, Apply Force Explosion, etc.
        // This is a good example of the Strategy Pattern.
        // https://en.wikipedia.org/wiki/Strategy_pattern
        // so i will create a based interface called IDeathEffect and then create a bunch of subclasses that inherit from it.
        // It should probably be a ScriptableObject so that it can be created in the editor and then assigned to the Death component.
    
        public void Init(Transform transform = null)
        {
            if(transform != null)
                _parent = transform;
        }

        public void Play()
        {
            if (particles.isPlaying || audioSource.isPlaying) return;
            PlayParticles();
            PlayAudio();
        }

        private void PlayParticles()
        {
            if (particles != null)
            {
                var activeParticles =  Object.Instantiate(particles, _parent);
                activeParticles.transform.localScale = Vector3.one * 3;
            }
        }

        private void PlayAudio()
        {
            if (audioClip != null && audioSource != null)
                audioSource.PlayOneShot(audioClip);
        }
        
        public IEnumerator Cleanup(Transform transform)
        {
            yield return new WaitForSeconds(1f);
            transform.DOScale(Vector3.zero, .25f).SetEase(Ease.OutCirc);
            
            if (audioClip != null)
                yield return new WaitForSeconds(ClipLength);
            
            // transform.gameObject.SetActive(false);
            // SetIsDead(true);
        }

        public void SetIsDead(bool value)
        {
            IsDead = value;
        }
        
        // public void PlayEffects()
        // {
        //     foreach (var effect in deathEffects)
        //     {
        //         effect.DoDeathEffect();
        //     }
        // }
    }
}

