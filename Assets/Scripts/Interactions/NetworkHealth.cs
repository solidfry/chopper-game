using System;
using Events;
using Interfaces;
using PlayerInteraction.Networking;
using Unity.Netcode;
using UnityEngine;
using Utilities;

namespace Interactions
{
    public class NetworkHealth : NetworkBehaviour, IDamageable
    {
        [SerializeField] int health = 200;
        [SerializeField] public NetworkVariable<int> networkHealth = new (100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        [SerializeField] Death death;
        private Collider[] _colliders;
        public event Action<int> SendHealthEvent;
        
        public override void OnNetworkSpawn()
        {
            Debug.Log("Player health spawned");
            if(IsClient && IsOwner || IsServer)
            {
                networkHealth.OnValueChanged += OnHealthChanged;
                _colliders = gameObject.GetComponentsInChildren<Collider>();
            }
            
        }
        
        public override void OnNetworkDespawn()
        {
            if(IsClient && IsOwner || IsServer)
                networkHealth.OnValueChanged -= OnHealthChanged;
        }
        
        public void TakeDamage(int damageAmount)
        {
            if(!IsServer) return;
            
            TakeDamageClientRpc(damageAmount);
        }
        
        [ClientRpc]
        private void TakeDamageClientRpc(int damageAmount)
        {
            if(!IsClient && !IsOwner) return;
            
            if(networkHealth.Value <= 0) return;

            networkHealth.Value -= damageAmount;
            Debug.Log(damageAmount + " damage taken" + " health is now " + networkHealth.Value);
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
            if(!IsServer) return;
            
            Debug.Log("Player died");
            if (!death.IsDead)
            {
                death.SetIsDead(true);
                
                SetColliders(false);
                
                GameEvents.OnPlayerDiedEvent?.Invoke(OwnerClientId);
            }
            
        }
        
        void SetColliders(bool value)
        {
            _colliders ??= gameObject.GetComponentsInChildren<Collider>();
            foreach (var c in _colliders)
            {
                c.enabled = value;
            }
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
            if (IsServer)
            {
                // if the player is colliding and is upside down in correlation to the environment
                if (other.gameObject.layer == LayerMask.NameToLayer("Environment") && Vector3.Dot(transform.up, Vector3.down) > 0)
                {
                    TakeDamageClientRpc(100);
                    Debug.Log("Hit your roof!");
                    return;
                }
                
                if(other.gameObject.layer == LayerMask.NameToLayer("Environment") && Speed.MetersPerSecondToKilometersPerHour(GetComponent<Rigidbody>().velocity.magnitude) > 50)
                {
                    TakeDamageClientRpc(200);
                    Debug.Log("Hit too hard!");
                    return;
                }
            }
            
            
                
        }
    }
    
}