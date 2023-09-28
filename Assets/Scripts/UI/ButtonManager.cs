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
        
        void Start()
        {
            button = GetComponent<Button>();
            text = GetComponentInChildren<TMP_Text>();
        }

        private void Update()
        {
            text.color = button.targetGraphic.canvasRenderer.GetColor();
        }

        public void LoadLevel(string sceneName) => SceneManager.LoadScene(sceneName);
        public void Quit() => Application.Quit();
    }
}
