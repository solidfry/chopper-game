using Enums;
using TMPro;
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
        public void Quit() => Application.Quit();
        // TODO: shut down the Netcode for Entities client/server worlds here once the ECS match exists.
        public void LeaveGame() => SceneManager.LoadScene(Scenes.MainMenu.ToString());
    }
}
