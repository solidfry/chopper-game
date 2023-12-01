using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GameAnalyticsSDK;
using Unity.Netcode;

public class AnalyticsTracking : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        GameAnalytics.Initialize();

        GameAnalytics.NewDesignEvent("Player_Landed_MainMenu", 0);
    }

}
