using UnityEngine;

namespace Networking
{
    public class ClientNetworkManager : MonoBehaviour
    {
        private void Awake()
        {
#if SERVER
          Destroy(this.gameObject);   
#endif
        }
    }
}
