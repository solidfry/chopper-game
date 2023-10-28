using System;
using Interfaces;
using Unity.Netcode;
using UnityEngine;
using Utilities;

namespace Interactions
{
    public class NetworkHealth : NetworkBehaviour, IDamageable
    {
        [SerializeField] int health = 200;
        [SerializeField] public NetworkVariable<int> networkHealth = new (100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        // [SerializeField] Death death;
        // [SerializeField] Collider collider3d;
        
        public event Action<int> SendHealthEvent;
        
        public override void OnNetworkSpawn()
        {
            Debug.Log("Player health spawned");
            if(IsClient && IsOwner || IsServer)
            {
                networkHealth.OnValueChanged += OnHealthChanged;
            }
        }
        
        public override void OnNetworkDespawn()
        {
            if(IsClient && IsOwner || IsServer)
                networkHealth.OnValueChanged -= OnHealthChanged;
        }
        
        public void TakeDamage(int damageAmount)
        {
            if(!IsServer || !IsOwner) return;
            
            if(networkHealth.Value <= 0) return;
            
            networkHealth.Value -= damageAmount;
        }
        
        private void OnHealthChanged(int previousvalue, int newvalue)
        {
            health = newvalue;
            Debug.Log("Health changed to " + newvalue);
            SendHealthEvent?.Invoke(health);
            if (newvalue <= 0)
            {
                Die();
            }
        }

        public void Die()
        {
            // if(!death.isDead)
            // {
            //     death.SetIsDead(true);
            //     death?.Play();
            //     
            //     Collider[] colliders = gameObject.GetComponentsInChildren<Collider>();
            //     
            //     if(colliders != null )
            //         foreach (var col in colliders)
            //         {
            //             collider3d.enabled = false;
            //         }
            //     
            //     // collider3d.enabled = false;
            //     Debug.Log($"{this.transform.name} died.");
            // }
        }
        
        public void InitialiseHealth()
        {
            if(IsClient && IsOwner) PlayerHealthSetServerRpc(health);
            
            // if(IsServer) PlayerHeathSetClientRpc(health);
            
            Debug.Log("Health initialised for " + OwnerClientId);
        }
        
        [ServerRpc] 
        private void PlayerHealthSetServerRpc(int health)
        {
            if(!IsServer) return;
            PlayerHealthSetClientRpc(health);
            Debug.Log("Server sent Client RPC to set health");
        }
        
        [ClientRpc]
        private void PlayerHealthSetClientRpc(int health)
        {
            networkHealth.Value = health;
            Debug.Log("Health set for " + OwnerClientId + "via Client RPC");
        }

        private void OnCollisionEnter(Collision other)
        {
            // Refactor this so that it's handled in a better way and in a different location
            if (IsOwner && IsClient || IsServer)
            {
                // if the player is colliding and is upside down in correlation to the environment
                if (other.gameObject.layer == LayerMask.NameToLayer("Environment") && Vector3.Dot(transform.up, Vector3.down) > 0)
                {
                    TakeDamage(100);
                    Debug.Log("Hit your roof!");
                    return;
                }
                
                if(other.gameObject.layer == LayerMask.NameToLayer("Environment") && Speed.MetersPerSecondToKilometersPerHour(GetComponent<Rigidbody>().velocity.magnitude) > 50)
                {
                    TakeDamage(200);
                    Debug.Log("Hit too hard!");
                    return;
                }
            }
            
            
                
        }
    }
    
}