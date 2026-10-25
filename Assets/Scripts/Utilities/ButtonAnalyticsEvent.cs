using UnityEngine;
using UnityEngine.UI;

namespace Utilities
{
    public class ButtonAnalyticsEvent : MonoBehaviour
    {
        Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();
        }

        // TODO: send $"Player_Pressed_{name}" via Unity Analytics (com.unity.services.analytics). GameAnalytics SDK was removed.
        private void Start() =>
            _button.onClick.AddListener(() => { });
    }
}
