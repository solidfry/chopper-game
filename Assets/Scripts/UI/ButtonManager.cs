using Enums;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UI
{
    public class ButtonManager : MonoBehaviour
    {
        Button button;
        private TMP_Text text;
        [SerializeField] bool useButtonColorForText = true;
        
        void Start()
        {
            button = GetComponent<Button>();
            text = GetComponentInChildren<TMP_Text>();
        }

        private void Update()
        {
            if (!useButtonColorForText) return;
            text.color = button.targetGraphic.canvasRenderer.GetColor();
        }

        public void LoadLevel(string sceneName) => SceneManager.LoadScene(sceneName);
        public void LoadLevelAsync(string sceneName) => SceneManager.LoadSceneAsync(sceneName);
        // public void LoadLevelAsyncViaEnum(Scenes scene) => SceneManager.LoadSceneAsync(scene.ToString());
        // public void LoadLevelAsyncViaEnumAdditive(Scenes scene) => SceneManager.LoadSceneAsync(scene.ToString(), LoadSceneMode.Additive);
        // public void NetworkLoadLevelViaEnum(Scenes scene) => NetworkManager.Singleton.SceneManager.LoadScene(scene.ToString(), LoadSceneMode.Single);
        public void Quit() => Application.Quit();
        public void LeaveGame()
        {
            if(NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();
            
            SceneManager.LoadScene(Scenes.MainMenu.ToString());
        }
    }
}
