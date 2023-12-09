using Events;
using UnityEngine;

namespace Audio
{
    public class UIAudioClipRequester : MonoBehaviour
    {
        public void Request(AudioClipListData list) => GameEvents.OnPlayRandomUISoundEvent?.Invoke(list.name);
    }
}