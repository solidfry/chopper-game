using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace UI
{
    public class UIKeyPressEvent : MonoBehaviour
    {

        [SerializeField] InputAction button;
    
        [SerializeField] UnityEvent doEvent;

        private void OnEnable()
        {
            button.Enable();
            button.performed += Do;
        }
    
        private void OnDisable()
        {
            button.Disable();
            button.performed -= Do;
        }
    
        private void Do(InputAction.CallbackContext obj)
        { 
            doEvent?.Invoke();
        }

    }
}
