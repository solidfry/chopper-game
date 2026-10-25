using Unity.Netcode;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// Lets the Training scene run on its own: creates the GameManager (and with it the NetworkManager)
    /// if the scene was not reached from Init, then starts a local host so one player can fly without a server.
    /// Temporary until the Netcode for Entities training range replaces it.
    /// </summary>
    public class TrainingBootstrap : MonoBehaviour
    {
        [SerializeField] GameManager gameManagerPrefab;
        [SerializeField] bool autoStartHost = true;

        private void Awake()
        {
            if (GameManager.Instance == null && gameManagerPrefab != null)
                Instantiate(gameManagerPrefab);
        }

        private void Start()
        {
            if (!autoStartHost) return;

            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogError("TrainingBootstrap: no NetworkManager found, cannot start host.");
                return;
            }

            if (!networkManager.IsListening)
                networkManager.StartHost();
        }
    }
}
