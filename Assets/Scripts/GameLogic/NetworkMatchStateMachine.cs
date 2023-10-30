using System.Collections.Generic;
using GameLogic.ScriptableObjects;
using GameLogic.StateMachine;
using GameLogic.StateMachine.MatchStateMachine;
using PlayerInteraction.Networking;
using Unity.Netcode;
using UnityEngine;

namespace GameLogic
{
    public class NetworkMatchStateMachine : NetworkBehaviour, IStateMachine
    {

        private MatchState CurrentState { get; set; }
        [SerializeField][ReadOnly] string matchStateName;
        
        public override  void OnNetworkSpawn()
        {
            if (!IsServer) return;
            
            ChangeState(new PreGame());
        }

        private void Update()
        {
            if (!IsServer) return;
            
            matchStateName = GetCurrentStateName();
            CurrentState.OnUpdate(this);
            
        }

        public void ChangeState(IState newState)
        {
            if (CurrentState != null)
            {
                CurrentState.OnExit();
            }

            CurrentState = (MatchState)newState;
            CurrentState.OnEnter(this);
        }

        public string GetCurrentStateName()
        {
            return CurrentState.GetType().Name;
        }
    }
}
