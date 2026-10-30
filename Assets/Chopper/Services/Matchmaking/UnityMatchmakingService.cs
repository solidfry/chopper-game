using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;
using UnityEngine;
using UnityEngine.Networking;

namespace Chopper.Services.Matchmaking
{
    /// <summary>
    /// Unity Matchmaker with the Edgegap allocator (plan section 4; Cloud Code module <c>CloudCode/ChopperAllocator</c>).
    /// Creates a ticket on the request's queue with the player's public IP (Edgegap starts the server near the
    /// players), polls until the matchmaker assigns a server, and returns its IP and game port. Cancelling deletes the
    /// ticket. Needs a signed-in player (<see cref="Auth.IAuthService"/>).
    /// </summary>
    public sealed class UnityMatchmakingService : IMatchmakingService
    {
        /// <summary>Ticket custom data key the allocator reads.</summary>
        public const string PlayerIpKey = "player_ip";
        private const string PublicIpUrl = "https://api.ipify.org";
        /// <summary>Seconds between ticket polls (the matchmaker limits how often a client may ask).</summary>
        private const float PollSeconds = 1.5f;

        public async UniTask<MatchAssignment> FindMatchAsync(MatchRequest request, IProgress<MatchmakingStatus> progress,
            CancellationToken cancellationToken)
        {
            if (!AuthenticationService.Instance.IsSignedIn)
                throw new InvalidOperationException("Not signed in to Unity services: online play needs a sign-in.");

            progress?.Report(MatchmakingStatus.Searching);
            var customData = new Dictionary<string, object> { ["chassis"] = request.ChassisId };
            var ip = await PublicIpAsync(cancellationToken);
            if (!string.IsNullOrEmpty(ip)) customData[PlayerIpKey] = ip;

            var players = new List<Player> { new(AuthenticationService.Instance.PlayerId, customData) };
            var ticket = await MatchmakerService.Instance.CreateTicketAsync(players, new CreateTicketOptions(request.Queue));
            var done = false;
            try
            {
                while (true)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(PollSeconds), cancellationToken: cancellationToken);
                    var status = await MatchmakerService.Instance.GetTicketAsync(ticket.Id);
                    if (status?.Value is not IpPortAssignment assignment) continue;

                    switch (assignment.Status)
                    {
                        case IpPortAssignment.StatusOptions.Found:
                            if (string.IsNullOrEmpty(assignment.Ip) || assignment.Port == null)
                                throw new InvalidOperationException("The matchmaker found a match but gave no server address.");
                            done = true;
                            progress?.Report(MatchmakingStatus.Connecting);
                            return new MatchAssignment(assignment.Ip, (ushort)assignment.Port.Value, assignment.MatchId);
                        case IpPortAssignment.StatusOptions.InProgress:
                            // A match id means players are matched and the server is starting.
                            progress?.Report(string.IsNullOrEmpty(assignment.MatchId) ? MatchmakingStatus.Searching : MatchmakingStatus.ServerStarting);
                            break;
                        case IpPortAssignment.StatusOptions.Failed:
                            done = true;
                            throw new InvalidOperationException($"Matchmaking failed: {assignment.Message}");
                        case IpPortAssignment.StatusOptions.Timeout:
                            done = true;
                            throw new TimeoutException("No match found in time.");
                    }
                }
            }
            finally
            {
                // Leaving the queue (cancel or error): the ticket must not stay in it.
                if (!done) DeleteTicket(ticket.Id);
            }
        }

        private static void DeleteTicket(string ticketId)
        {
            MatchmakerService.Instance.DeleteTicketAsync(ticketId).AsUniTask().Forget(
                e => Debug.LogWarning($"Matchmaking: could not delete ticket {ticketId}: {e.Message}"));
        }

        /// <summary>The player's public IP, for server placement; null if it cannot be found (Edgegap then picks a default location).</summary>
        private static async UniTask<string> PublicIpAsync(CancellationToken cancellationToken)
        {
            using var web = UnityWebRequest.Get(PublicIpUrl);
            web.timeout = 5;
            try
            {
                await web.SendWebRequest().WithCancellation(cancellationToken);
                return web.result == UnityWebRequest.Result.Success ? web.downloadHandler.text.Trim() : null;
            }
            catch (UnityWebRequestException e)
            {
                Debug.LogWarning($"Matchmaking: public IP lookup failed ({e.Message}); the server location may be far away.");
                return null;
            }
        }
    }
}
