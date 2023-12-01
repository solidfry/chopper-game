using System.Collections;
using System.Collections.Generic;
using GameAnalyticsSDK;
using UnityEngine;
using UnityEngine.UI;

public class ButtonAnalyticsEvent : MonoBehaviour
{
    Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
    }

    private void Start()
    {
        _button.onClick.AddListener(() =>
        {
            GameAnalytics.NewDesignEvent($"Player_Pressed_{this.gameObject.name}", 0);
        });
    }
}
