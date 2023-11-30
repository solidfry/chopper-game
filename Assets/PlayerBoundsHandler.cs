using System;
using Events;
using Interfaces;
using UI;
using UnityEngine;
using Utilities;

public class PlayerBoundsHandler : MonoBehaviour
{
    [Header("Timer")]
    [SerializeField] float countdownTime = 5f;
    [SerializeField] CountdownTimer timer;
    [SerializeField] TimerUI timerUI;
    [Header("Bounds")]
    [SerializeField] SphereCollider sphereCollider;
    [SerializeField] private float boundsRadius = 1200f;
    [SerializeField] [ReadOnly] bool playerOutOfBounds = false;
    ulong _playerId;
    
    const float EPSILON = 0.01f;

    private void Start()
    {
        sphereCollider = GetComponent<SphereCollider>();
        timer = new CountdownTimer(countdownTime, Time.deltaTime);
        if(timerUI != null)
        {
            InitialiseTimer();
        }
    }

    private void FixedUpdate()
    {
        if (playerOutOfBounds)
        {
            timer.OnUpdate();
            if(timerUI != null)
            {
                timerUI.SetTimer(timer.CurrentTimeRemaining);
            }

            if(timer.CurrentTimeRemaining <= EPSILON)
            {
                Debug.Log("Player died was out of bounds too long!");
                GameEvents.OnPlayerOutOfBoundsDestroyEvent?.Invoke(_playerId);
                Debug.Log("Current timer remaining was less than .5f");
                StopAndResetTimer();
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.layer == LayerMask.NameToLayer("LocalPlayer") && !playerOutOfBounds )
        {
            playerOutOfBounds = true;
            GameEvents.OnPlayerOutOfBoundsEvent?.Invoke();

            Debug.Log("Player went out of bounds");
            
            if(timer.GetTimerStatus == false)
            {
                StartTimer();
            }
            
            _playerId = other.GetComponentInParent<IPlayer>().PlayerOwnerNetworkId;
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if(other.gameObject.layer  == LayerMask.NameToLayer("LocalPlayer") && playerOutOfBounds)
        {
            playerOutOfBounds = false;
            GameEvents.OnPlayerInBoundsEvent?.Invoke();

            Debug.Log("Player went in bounds");
            StopAndResetTimer();
        }
    }
    
    private void InitialiseTimer()
    {
        timerUI.SetColors();
        timerUI.SetTimer(countdownTime);
        timerUI.Hide();
        timerUI.gameObject.SetActive(false);
    }
    
    private void StartTimer()
    {
        if(!timerUI.isActiveAndEnabled) 
            timerUI.gameObject.SetActive(true);
        
        timerUI.Show();
        timer.StartTimer();
    }
    
    private void StopAndResetTimer()
    {
        timer.StopTimer();
        timer.ResetTimer(countdownTime);
        timerUI.Hide();
    }
    
    private void SetBoundsRadius()
    {
        if(sphereCollider is null) return;
        sphereCollider.radius = boundsRadius;        
    }

    private void OnValidate()
    {
        if(sphereCollider is null) 
            sphereCollider = GetComponent<SphereCollider>();
        
        SetBoundsRadius();
    }
}
