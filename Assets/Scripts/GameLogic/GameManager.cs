using System.Collections.Generic;
using GameLogic.ScriptableObjects;
using PlayerInteraction.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public class GameManager : SingletonPersistent<GameManager>
    {
        [SerializeField] NetworkManager networkManagerPrefab;
        
        public override void Awake()
        {
            base.Awake();

            if (NetworkManager.Singleton == null)
            {
                Debug.Log("NetworkManager prefab spawned");
                Instantiate(networkManagerPrefab);
            }
        }
    }
}