using Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace UI
{
    public class MatchMenuUI : MonoBehaviour
    {
        [Header("State")]
        [SerializeField][ReadOnly] private bool menuOpen;
        
        [Header("Show UI Input")]
        [SerializeField] InputAction showAction;
    
        [Space]
        [Header("Unity Events")]
        [Space]
        [SerializeField] UnityEvent onShown;
        [SerializeField] UnityEvent onHidden;

        private void OnEnable()
        {
            showAction.Enable();
            showAction.performed += Toggle;
        }
        
        private void OnDisable()
        {
            showAction.Disable();
            showAction.performed -= Toggle;
        }

        public void Toggle(InputAction.CallbackContext obj)
        {
            Debug.Log("Toggled");
            if (menuOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        private void Open()
        {
            menuOpen = true;
            GameEvents.OnShowCursorEvent?.Invoke();
            GameEvents.OnTogglePlayerControlsEvent?.Invoke(false);
            onShown?.Invoke();
            Debug.Log("Open");
        }

        private void Close()
        {
            menuOpen = false;
            GameEvents.OnHideCursorEvent?.Invoke();
            GameEvents.OnTogglePlayerControlsEvent?.Invoke(true);
            onHidden?.Invoke();
            Debug.Log("Close");
        }
    }
}
