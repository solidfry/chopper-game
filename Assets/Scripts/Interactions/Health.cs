using System.Collections;
using DG.Tweening;
using Interfaces;
using UnityEngine;

namespace Interactions
{
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private int health = 100;
        [SerializeField] private Death death;
        [SerializeField][ReadOnly] bool isDead;
        
        [SerializeField] Collider collider3d;
        Coroutine cleanup;
        
        private void Start()
        {
            collider3d = GetComponent<Collider>();
            death.Init(transform);
        }

        public void TakeDamage(int damageAmount)
        {
            if(health <= 0) return;
            
            health -= damageAmount;
            if (health <= 0)
            {
                Die();
            }
        }

        public void Die()
        {
            if(!isDead)
            {
                isDead = true;
                death?.Play();
                collider3d.enabled = false;
                Debug.Log($"{this.transform.name} died.");
                
                cleanup = death is not null ? StartCoroutine(death.Cleanup(this.transform)) : cleanup = null;
            }
        }
        
        private void OnDestroy()
        {
            if(cleanup != null)
                StopCoroutine(cleanup);
            
            transform.DOKill();
        }
    }
}