using Events;
using UnityEngine;

namespace Audio
{
    public class AudioClipRequestHandler : MonoBehaviour
    {
        public void Request(AudioClipListData list) => GameEvents.onPlayRandomUISoundEvent?.Invoke(list.name);
    }
}