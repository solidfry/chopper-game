using Events;
using UnityEngine;

namespace Audio
{
    public class MusicRequester : MonoBehaviour
    {
        [SerializeField] AudioClip clipToRequest;
    
        void Start()
        {
            if (clipToRequest == null) return;   
            GameEvents.OnMusicChangedEvent?.Invoke(clipToRequest);
            Debug.Log($"Music clip requested {clipToRequest.name}");
        }
    }
}
