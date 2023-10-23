using UnityEngine;
using Button = UnityEngine.UI.Button;

namespace UI
{
    public class NavButtonHandler : MonoBehaviour
    {
        [SerializeField] bool isActiveView = false;
        [SerializeField] Color activeColor;
        [SerializeField] Color normalColor;
        private Button _button;

        private void Awake()
        {
            normalColor = GetComponent<Button>().colors.normalColor;
            _button = GetComponent<Button>();
        }

        private void Update() => SetColors();

        private void SetColors()
        {
            if (!IsInteractable) return;
            
            if (isActiveView)
                _button.targetGraphic.canvasRenderer.SetColor(activeColor);
            else
                _button.targetGraphic.canvasRenderer.SetColor(normalColor);
        }

        public void SetActive()
        {
            if(!IsInteractable) return;
            isActiveView = true;
        }
    
        public void SetInactive()
        {
            if(!IsInteractable) return;
            isActiveView = false;
        }
        
        public bool IsInteractable => _button.IsInteractable();
    }
}
