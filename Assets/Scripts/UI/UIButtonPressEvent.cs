using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UI
{
    public class UIButtonPressEvent : MonoBehaviour
    {
        [SerializeField] UnityEvent doEvent;
        private Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();
        }
        
        private void OnEnable()
        {
            _button.onClick.AddListener(()=>
            {
                doEvent?.Invoke();
            });
        }
    }
}