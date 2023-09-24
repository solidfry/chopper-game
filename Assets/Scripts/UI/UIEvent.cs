using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace UI
{
    public class UIEvent : MonoBehaviour
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
