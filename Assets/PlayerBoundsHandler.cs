using Events;
using UnityEngine;

public class PlayerBoundsHandler : MonoBehaviour
{
    [SerializeField] LayerMask layerMask;
    private void OnTriggerExit(Collider other)
    {
        if(other.gameObject.layer == layerMask.value)
            GameEvents.OnPlayerOutOfBoundsEvent?.Invoke();

        Debug.Log("Player went out of bounds");
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if(other.gameObject.layer == layerMask.value)
            GameEvents.OnPlayerInBoundsEvent?.Invoke();
        
        Debug.Log("Player went in bounds");
    }
}
