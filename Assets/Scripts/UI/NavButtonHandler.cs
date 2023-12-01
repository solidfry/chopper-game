using UnityEngine;
using Button = UnityEngine.UI.Button;
using UnityEngine.UI;

namespace UI
{
    public class NavButtonHandler : MonoBehaviour
    {
        [SerializeField] bool isActiveView = false;

        [SerializeField] Color normalColor;
        [SerializeField] Color activeColor;

        private Button _button;

        ColorBlock colors;

        private void Awake()
        {
            _button = GetComponent<Button>();
            SetButtonColors(false);
        }


        ColorBlock SetButtonColors(bool isActive)
        {
            colors = _button.colors;
            colors.normalColor = isActive ? activeColor : normalColor;
            return colors;
        }

        public void SetActive()
        {
            if (!IsInteractable) return;
            isActiveView = true;
            _button.colors = SetButtonColors(isActiveView);
            _button.Select();
        }

        public void SetInactive()
        {
            if (!IsInteractable) return;
            isActiveView = false;
            _button.colors = SetButtonColors(isActiveView);
        }

        public bool IsInteractable => _button.IsInteractable();

        public Button GetButton => _button;
    }
}
