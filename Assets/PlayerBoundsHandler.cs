using Events;
using UnityEngine;

public class PlayerBoundsHandler : MonoBehaviour
{
    private void OnTriggerExit(Collider other)
    {
        GameEvents.OnPlayerOutOfBoundsEvent?.Invoke();

        Debug.Log("Player went out of bounds");
    }
    
    private void OnTriggerEnter(Collider other)
    {
        GameEvents.OnPlayerInBoundsEvent?.Invoke();
        
        Debug.Log("Player went in bounds");
    }
}
