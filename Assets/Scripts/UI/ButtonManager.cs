using UnityEngine;
using UnityEngine.SceneManagement;

namespace UI
{
    public class ButtonManager : MonoBehaviour
    {
        public void LoadLevel(string sceneName) => SceneManager.LoadScene(sceneName);
        public void Quit() => Application.Quit();
    }
}
