using Enums;
using Events;
using UnityEngine;

namespace Audio
{
    public class MusicRequester : MonoBehaviour
    {
        [SerializeField] AudioClip clipToRequest;
        [SerializeField] TrackPlayMode playMode = TrackPlayMode.PlayOnce;
    
        void Start()
        {
            if (clipToRequest == null) return;   
            GameEvents.OnMusicChangedEvent?.Invoke(clipToRequest, playMode);
            Debug.Log($"Music clip requested {clipToRequest.name}");
        }
    }
}
