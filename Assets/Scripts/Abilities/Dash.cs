using System;
using UnityEngine;

namespace Abilities
{
    [Serializable]
    public class Dash : IAbility
    {

        [SerializeField] float force = 100000f;
        [SerializeField] float cooldown = 1f;
        [SerializeField] bool canDash = true;

        public bool CanDash => canDash;
        public float Cooldown => cooldown;

        [SerializeField] private Rigidbody rb;

        [SerializeField] float cooldownTimer = 0f;

        public void OnStart(Rigidbody rigidbody)
        {
            canDash = true;
            if (rb == null)
                rb = rigidbody;
        }

        public void OnUpdate()
        {
            if (!canDash)
                cooldownTimer -= Time.deltaTime;

            if (cooldownTimer <= 0)
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
            if (!canDash && rb != null) return;

            Debug.Log("Dash");
            rb.AddForce(rb.velocity * force * Time.fixedDeltaTime, ForceMode.Impulse);
            canDash = false;
        }
    }
}