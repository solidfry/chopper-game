using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class GameModeSelectionManager : MonoBehaviour
{
    
    [SerializeField] List<Button> gameModeButtons = new ();
    [SerializeField] private Button startServer;

    private void Awake()
    {
        AssignButtonListeners();
    }

    private void OnEnable()
    {
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
    }
    
    private void OnDestroy()
    {
        NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
    }

    private void OnServerStarted()
    {
        startServer.gameObject.SetActive(false);
    }

    void AssignButtonListeners()
    {
        foreach (var button in gameModeButtons)
        {
            button.onClick.AddListener(() =>
            {
                NetworkManager.Singleton.StartClient();
                Debug.Log("Client Started");
            });
        }

        if (startServer != null) startServer.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartServer();
            Debug.Log("Server started");
        });
    }
}
