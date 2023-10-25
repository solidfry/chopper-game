using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class GameModeSelectionManager : MonoBehaviour
{
    
    [SerializeField] List<Button> gameModeButtons = new ();

    private void Awake()
    {
        AssignButtonListeners();
    }

    // Update is called once per frame
    void Update()
    {
        
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
    }
}
