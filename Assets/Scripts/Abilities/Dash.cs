using System;
using UnityEngine;

namespace Abilities
{
    [Serializable]
    public class Dash : IAbility
    {
        
        [SerializeField] float force = 1000f;
        [SerializeField] float cooldown = 1f;
        [SerializeField] bool canDash = true;
        
        private Rigidbody rb;
        
        float cooldownTimer = 0f;

        public Dash(Rigidbody _rb)
        { 
            rb = _rb;
        }

        public void OnStart()
        {
            canDash = true;
        }

        public void OnUpdate()
        {
            if(!canDash)
                cooldownTimer -= Time.deltaTime;
            
            if(cooldownTimer <= 0)
            {
                canDash = true;
                cooldownTimer = cooldown;
            }
        }

        public void OnFixedUpdate()
        {
            
        }

        public void DoAbility()
        {
            if(!canDash && rb != null) return;
            
            rb.AddForce(rb.velocity * force * Time.fixedDeltaTime, ForceMode.Impulse);
            canDash = false;
        }
    }
}