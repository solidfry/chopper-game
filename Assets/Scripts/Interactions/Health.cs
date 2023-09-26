using System.Collections;
using DG.Tweening;
using Interfaces;
using UnityEngine;

namespace Interactions
{
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private int health = 100;
        [SerializeField] private Death onDeath;
        [SerializeField][ReadOnly] bool isDead;
        
        [SerializeField] Collider collider3d;
        Coroutine cleanup;
        
        private void Start()
        {
            collider3d = GetComponent<Collider>();
            onDeath.Init(transform);
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
                onDeath?.Play();
                collider3d.enabled = false;
                Debug.Log($"{this.transform.name} died.");
                cleanup = StartCoroutine(Cleanup());
            }
        }

        IEnumerator Cleanup()
        {
            yield return new WaitForSeconds(5f);
            transform.DOScale(Vector3.zero, .25f).SetEase(Ease.OutCirc);
            yield return new WaitForSeconds(.5f);
            Destroy(this.gameObject);
        }
        
        private void OnDestroy()
        {
            if(cleanup != null)
                StopCoroutine(cleanup);
            
            transform.DOKill();
        }
    }
}