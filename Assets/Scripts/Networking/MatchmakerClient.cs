using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Server;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;
using StatusOptions = Unity.Services.Matchmaker.Models.MultiplayAssignment.StatusOptions;
using UnityEngine;

namespace Networking
{
    public class MatchmakerClient : MonoBehaviour
    {

        private string _ticketId;

        private void OnEnable()
        {
            ServerStartup.ClientInstance += SignIn;
        }

        private void OnDisable()
        {
            ServerStartup.ClientInstance -= SignIn;
        }

        private async void SignIn()
        {
            await ClientSignIn("MatchmakerClient");
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private async Task ClientSignIn(string serviceProfileName = null)
        {
            if (serviceProfileName != null)
            {

                var initOptions = new InitializationOptions();
                initOptions.SetProfile(serviceProfileName);
                await UnityServices.InitializeAsync(initOptions);
            }
            else
            {
                await UnityServices.InitializeAsync();
            }

            Debug.Log($"Signed in Anonymously as {serviceProfileName}({PlayerID()})");
        }

        private string PlayerID() => AuthenticationService.Instance.PlayerId;

        // TODO: Need to have this called in the UI somewhere.
        // TODO: This should be done when the player clicks the Multiplayer button in the main menu and is sent to a lobby.
        public void StartClient(string queueName = "TestMode")
        {
            CreateATicket(queueName);
        }

        private async void CreateATicket(string queueName = "TestMode")
        {
            var options = new CreateTicketOptions(queueName);
            var players = new List<Player>
            {
                new Player (PlayerID(),
                    new MatchmakingPlayerData()
                    {
                        // PlayerName = "Player " + PlayerID(),
                        Skill = 100
                    }
                )
            };

            var ticketResponse = await MatchmakerService.Instance.CreateTicketAsync(players, options);
            _ticketId = ticketResponse.Id;
            Debug.Log($"Ticket ID: {_ticketId}");
            PollTicketStatus();
        }

        private async void PollTicketStatus()
        {
            MultiplayAssignment multiplayAssignment = null;
            bool gotAssignment = false;
            do
            {
                await Task.Delay(TimeSpan.FromSeconds(1f));
                var ticketStatus = await MatchmakerService.Instance.GetTicketAsync(_ticketId);
                if (ticketStatus == null) continue;

                if (ticketStatus.Type == typeof(MultiplayAssignment))
                {
                    multiplayAssignment = ticketStatus.Value as MultiplayAssignment;
                }

                if (multiplayAssignment != null)
                    switch (multiplayAssignment.Status)
                    {
                        case StatusOptions.Found:
                            TicketAssigned(multiplayAssignment);
                            gotAssignment = true;
                            break;
                        case StatusOptions.InProgress:
                            break;
                        case StatusOptions.Failed:
                            Debug.LogError($"Failed to get ticket status. Error: {multiplayAssignment.Message}");
                            gotAssignment = true; // just to stop the polling
                            break;
                        case StatusOptions.Timeout:
                            Debug.LogError("Failed to get ticket status. Ticket timed out.");
                            gotAssignment = true; // just to stop the polling
                            break;
                        default:
                            throw new InvalidOperationException();
                    }
            }
            while (!gotAssignment);
        }

        private void TicketAssigned(MultiplayAssignment assignment)
        {
            if (assignment.Port != null)
            {
                Debug.Log($"Ticket assigned: {assignment.Ip}{assignment.Port}");

                NetworkManager.Singleton.GetComponent<UnityTransport>()
                    .SetConnectionData(assignment.Ip, (ushort)assignment.Port);

                NetworkManager.Singleton.StartClient();
            }
        }

        [Serializable]
        public class MatchmakingPlayerData
        {
            // public string PlayerName;
            public int Skill;
        }
    }
}
