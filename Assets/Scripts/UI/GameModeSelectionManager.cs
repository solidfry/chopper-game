using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class GameModeSelectionManager : MonoBehaviour
    {

        [SerializeField] List<Button> gameModeButtons = new ();
        [SerializeField] Button startServer;

        // TODO: the "start server" button used Netcode for GameObjects. Rewire it to the Netcode for Entities
        // bootstrap (or remove it) when matchmaking moves to Multiplayer Services in Phase 3.
        private void Awake()
        {
            if (startServer != null)
                startServer.gameObject.SetActive(false);
        }
    }
}
