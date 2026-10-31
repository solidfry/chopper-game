using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Apis.Matchmaker;
using Unity.Services.CloudCode.Core;
using Unity.Services.Matchmaker.Model;

namespace ChopperAllocator;

public sealed class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        // Unity's example modules still use Create(); its replacement is not documented in this package version.
#pragma warning disable CS0618
        config.Dependencies.AddSingleton(GameApiClient.Create());
#pragma warning restore CS0618
    }
}

/// <summary>
/// Unity Matchmaker allocator for Edgegap (plan section 4). A pool of the matchmaker queue with hosting type
/// "Cloud Code" calls <see cref="Allocate"/> once per match: it starts an Edgegap deployment near the players (their
/// public IPs come in the ticket custom data, key <c>player_ip</c>). The matchmaker then calls <see cref="Poll"/>
/// until the server is ready and hands its IP and game port to the players. The Edgegap API token is a Unity secret.
/// Empty servers stop themselves (the game server's idle stop), so nothing here needs to clean up.
/// </summary>
public sealed class EdgegapAllocator(IGameApiClient gameApi, ILogger<EdgegapAllocator> logger) : IMatchmakerAllocator
{
    /// <summary>Edgegap application and version to deploy, and the port players connect to.</summary>
    private const string Application = "airwingnitro";
    private const string Version = "0.1.2";
    private const string GamePort = "gameport";
    /// <summary>Unity Dashboard → Administration → Secrets: the Edgegap API token (not the registry token).</summary>
    private const string TokenSecret = "edgegap1";
    /// <summary>Ticket custom data key with the player's public IP (the game client fills it).</summary>
    private const string PlayerIpKey = "player_ip";
    private const string EdgegapApi = "https://api.edgegap.com";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    [CloudCodeFunction("Matchmaker_AllocateServer")]
    public async Task<AllocateResponse> Allocate(IExecutionContext context, AllocateRequest request)
    {
        try
        {
            var users = new JArray();
            foreach (var ip in PlayerIps(request.MatchmakingResults.MatchProperties))
                users.Add(new JObject { ["user_type"] = "ip_address", ["user_data"] = new JObject { ["ip_address"] = ip } });
            if (users.Count == 0)
            {
                logger.LogWarning("No player_ip in the tickets; Edgegap picks a default location.");
                users.Add(new JObject
                {
                    ["user_type"] = "geo_coordinates",
                    ["user_data"] = new JObject { ["latitude"] = 35.89, ["longitude"] = -106.26 },
                });
            }

            var body = new JObject
            {
                ["application"] = Application,
                ["version"] = Version,
                ["require_cached_locations"] = false,
                ["users"] = users,
                ["environment_variables"] = new JArray
                {
                    new JObject { ["key"] = "MATCH_ID", ["value"] = request.MatchId, ["is_hidden"] = false },
                },
                ["tags"] = new JArray("ugs-matchmaker"),
            };

            using var message = await Send(context, HttpMethod.Post, "/v2/deployments", body);
            var text = await message.Content.ReadAsStringAsync();
            if (!message.IsSuccessStatusCode)
            {
                logger.LogError("Edgegap deployment failed ({Status}): {Body}", message.StatusCode, text);
                return new AllocateResponse(AllocateStatus.Error) { Message = text };
            }

            var requestId = JObject.Parse(text).Value<string>("request_id") ?? "";
            return new AllocateResponse(AllocateStatus.Created)
            {
                AllocationData = new Dictionary<string, object> { ["requestId"] = requestId },
            };
        }
        catch (Exception e)
        {
            logger.LogError(e, "Edgegap deployment failed");
            return new AllocateResponse(AllocateStatus.Error) { Message = e.Message };
        }
    }

    [CloudCodeFunction("Matchmaker_PollAllocation")]
    public async Task<PollResponse> Poll(IExecutionContext context, PollRequest request)
    {
        try
        {
            var requestId = request.AllocationData["requestId"]?.ToString() ?? "";
            using var message = await Send(context, HttpMethod.Get, $"/v1/status/{requestId}", null);
            var text = await message.Content.ReadAsStringAsync();
            if (!message.IsSuccessStatusCode) return new PollResponse(PollStatus.Error) { Message = text };

            var status = JObject.Parse(text);
            var state = status.Value<string>("current_status") ?? "";
            if (state.EndsWith("READY", StringComparison.OrdinalIgnoreCase))
            {
                var ip = status.Value<string>("public_ip");
                var port = status["ports"]?[GamePort]?.Value<int?>("external");
                if (string.IsNullOrEmpty(ip) || port == null)
                    return new PollResponse(PollStatus.Error) { Message = $"Ready, but no public IP or port '{GamePort}': {text}" };
                return new PollResponse(PollStatus.Allocated) { AssignmentData = AssignmentData.IpPort(ip, port.Value) };
            }
            if (state.EndsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                return new PollResponse(PollStatus.Error) { Message = status.Value<string>("error_detail") ?? text };
            if (state.Contains("TERMINAT", StringComparison.OrdinalIgnoreCase))
                return new PollResponse(PollStatus.Error) { Message = "The deployment ended before players could join." };
            return new PollResponse(PollStatus.Pending);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Polling Edgegap failed");
            return new PollResponse(PollStatus.Error) { Message = e.Message };
        }
    }

    /// <summary>One Edgegap API call with the token from the Unity secret ("token &lt;value&gt;").</summary>
    private async Task<HttpResponseMessage> Send(IExecutionContext context, HttpMethod method, string path, JObject? body)
    {
        var token = (await gameApi.SecretManager.GetSecret(context, TokenSecret)).Value.Trim();
        using var message = new HttpRequestMessage(method, EdgegapApi + path);
        message.Headers.TryAddWithoutValidation("Authorization", token.StartsWith("token ", StringComparison.OrdinalIgnoreCase) ? token : "token " + token);
        if (body != null) message.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        return await Http.SendAsync(message);
    }

    /// <summary>Valid public IPs from the players' ticket custom data.</summary>
    private static IEnumerable<string> PlayerIps(Dictionary<string, object> matchProperties)
    {
        if (!matchProperties.TryGetValue("Players", out var players) || players is not JArray list) yield break;
        foreach (var player in list)
        {
            var ip = player["CustomData"]?[PlayerIpKey]?.Value<string>() ?? player["customData"]?[PlayerIpKey]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(ip) && IPAddress.TryParse(ip, out _)) yield return ip;
        }
    }
}
