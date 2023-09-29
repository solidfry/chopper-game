using System.Collections;
using System.Collections.Generic;
using Events;
using UnityEngine;

public class SendPlayer : MonoBehaviour
{
    private void Awake()
    {
        GameEvents.onSendPlayerEvent?.Invoke(transform);
    }
}
