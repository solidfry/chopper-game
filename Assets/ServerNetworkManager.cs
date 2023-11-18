using UnityEngine;

public class ServerNetworkManager : MonoBehaviour
{
    private void Awake()
    {
#if !SERVER
          Destroy(this.gameObject);   
#endif
    }
}
