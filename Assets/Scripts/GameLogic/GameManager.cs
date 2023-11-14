using Audio;
using UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Utilities;

namespace GameLogic
{
    public class GameManager : SingletonPersistent<GameManager>
    {
        [SerializeField] NetworkManager networkManagerPrefab;
        [SerializeField] CursorManager cursorManager;
        [SerializeField] UIAudioManager uiAudioManager;
        [SerializeField] ColourManager colourManager;
        [SerializeField] NotificationManager notificationManager;
        
        public override void Awake()
        {
            base.Awake();

            if (NetworkManager.Singleton == null) 
                Instantiate(networkManagerPrefab);

            if(CursorManager.Instance == null)
                Instantiate(cursorManager);
            
            if(UIAudioManager.Instance == null)
                Instantiate(uiAudioManager);
            
            if(ColourManager.Instance == null)
                Instantiate(colourManager);
            
            if(NotificationManager.Instance == null)
                Instantiate(notificationManager);
        }
        
        public void QuitGame()
        {
            Application.Quit();
        }
        
        public void LoadScene(string sceneName)
        {
            SceneManager.LoadSceneAsync(sceneName);
        }
    }
}