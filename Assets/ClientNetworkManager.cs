using UnityEngine;

public class ClientNetworkManager : MonoBehaviour
{
    private void Awake()
    {
#if SERVER
          Destroy(this.gameObject);   
#endif
    }
}
