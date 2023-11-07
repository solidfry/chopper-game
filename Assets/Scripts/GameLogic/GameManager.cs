using Unity.Netcode;
using UnityEngine;

namespace GameLogic
{
    public class GameManager : SingletonPersistent<GameManager>
    {
        [SerializeField] NetworkManager networkManagerPrefab;
        
        public override void Awake()
        {
            base.Awake();

            if (NetworkManager.Singleton != null) return;
            
            Debug.Log("NetworkManager prefab spawned");
            Instantiate(networkManagerPrefab);
        }
    }
}