using Events;
using UnityEngine;

namespace Audio
{
    public class AudioClipRequestHandler : MonoBehaviour
    {
        public void Request (string soundListName) => GameEvents.onPlayRandomUISoundEvent.Invoke(soundListName);
    }
}