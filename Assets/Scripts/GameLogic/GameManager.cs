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
        
        [Header("System Prefabs")]
        [SerializeField] NetworkManager networkManagerPrefab;
        [SerializeField] CursorManager cursorManagerPrefab;
        [SerializeField] UIAudioManager uiAudioManagerPrefab;
        [SerializeField] ColourManager colourManagerPrefab;
        [SerializeField] NotificationManager notificationManagerPrefab;
        [SerializeField] MusicManager musicManagerPrefab;
        
        [field: Header("Active Systems")]
        [field:SerializeField ] 
        public NetworkManager NetworkManager { get; private set; }
        private bool _networkManagerIsActive;
        [field:SerializeField ] 
        public CursorManager CursorManager { get; private set; }
        private bool _cursorManagerIsActive;
        [field:SerializeField ] 
        public ColourManager ColourManager { get; private set; }
        private bool _colourManagerIsActive;
        [field:SerializeField ] 
        public NotificationManager NotificationManager { get; private set; }
        private bool _notificationManagerIsActive;
        [field:SerializeField ] 
        public UIAudioManager UIAudioManager { get; private set; }
        private bool _uiAudioManagerIsActive;
        [field:SerializeField ]
        public MusicManager MusicManager { get; private set; }
        private bool _musicManagerIsActive;
        
        [SerializeField] [ReadOnly] 
        private bool allSystemsActive;

        public override void Awake()
        {
#if SERVER
            Destroy(this.gameObject);  
            Debug.Log("Destroyed GameManager on Server");
#endif
            base.Awake(); 
            SetSystemsActive();
        }

        private void Start()
        {
            CheckAllSystemsActive();
        }
        
        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        
        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void SetSystemsActive()
        {
            if (NetworkManager == null) 
                NetworkManager = Instantiate(networkManagerPrefab);
            
            if(CursorManager == null)
                CursorManager = Instantiate(cursorManagerPrefab);
            
            if(UIAudioManager == null)
                UIAudioManager = Instantiate(uiAudioManagerPrefab);
            
            if(ColourManager == null)
                ColourManager = Instantiate(colourManagerPrefab);
            
            if(NotificationManager == null)
                NotificationManager = Instantiate(notificationManagerPrefab);
            
            if(MusicManager == null)
                MusicManager = Instantiate(musicManagerPrefab);
        }
        
        public bool CheckAllSystemsActive()
        {
            _networkManagerIsActive = NetworkManager != null;
            _cursorManagerIsActive = CursorManager != null;
            _uiAudioManagerIsActive = UIAudioManager != null;
            _colourManagerIsActive = ColourManager != null;
            _notificationManagerIsActive = NotificationManager != null;
            _musicManagerIsActive = MusicManager != null;
            
            allSystemsActive = 
                _networkManagerIsActive 
                               && _cursorManagerIsActive 
                               && _uiAudioManagerIsActive 
                               && _colourManagerIsActive 
                               && _notificationManagerIsActive 
                               && _musicManagerIsActive;
            
            return allSystemsActive;
        }
        
        private void OnSceneLoaded(Scene arg0, LoadSceneMode arg1) => CheckAllSystemsActive();

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