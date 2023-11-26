using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class GameModeSelectionManager : MonoBehaviour
    {
    
        [SerializeField] List<Button> gameModeButtons = new ();
        [SerializeField] Button startServer;

        private void Awake()
        {
            AssignButtonListeners();
        
            if(NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        }
    
        private void OnDestroy()
        {
            if(NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        }

        private void OnServerStarted() => startServer.gameObject.SetActive(false);

        void AssignButtonListeners()
        {
            // foreach (var button in gameModeButtons)
            // {
            //     button.onClick.AddListener(() =>
            //     {
            //         NetworkManager.Singleton.StartClient();
            //         Debug.Log("Client Started");
            //     });
            // }

            if (startServer != null) startServer.onClick.AddListener(() =>
            {
                NetworkManager.Singleton.StartServer();
                Debug.Log("Server started");
            });
        }
    }
}
