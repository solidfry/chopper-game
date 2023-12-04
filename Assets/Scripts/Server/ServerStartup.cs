using System;
using System.Threading.Tasks;
using Networking;
using Newtonsoft.Json;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;
using Unity.Services.Multiplay;
using UnityEngine;

namespace Server
{
    public class ServerStartup : MonoBehaviour
    {

        public static event Action ClientInstance;

        const ushort MAXPLAYERS = 12;
        private const string InternalServerIP = "0.0.0.0";
        private string _externalServerIP = "0.0.0.0";
        private ushort _serverPort = 7777;
        private string ExternalConnectionString => $"{_externalServerIP}:{_serverPort}";
        const int MultiplayServiceTimeout = 20000;

        private string _allocationId;

        const int _ticketCheckMs = 1000;
        private bool _backfilling = false;

        IServerEvents _serverEvents;
        IMultiplayService _multiplayService;
        MultiplayEventCallbacks _serverEventCallbacks;
        MatchmakingResults _matchmakerPayload;
        CreateBackfillTicketOptions _createBackfillTicketOptions;
        IServerQueryHandler _serverQueryHandler;
        BackfillTicket _localBackfillTicket;
        private bool IsServer;

        async void Start()
        {
            IsServer = false;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-dedicatedServer")
                    IsServer = true;

                if (args[i] == "-port" && (i + 1 < args.Length))
                    _serverPort = (ushort)int.Parse(args[i + 1]);

                if (args[i] == "-ip" && (i + 1 < args.Length))
                    _externalServerIP = args[i + 1];

            }

            if (IsServer)
            {
                StartServer();
                await StartServerServices();
            }
            else
            {
                ClientInstance?.Invoke();
            }
        }

        private void StartServer()
        {
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(InternalServerIP, _serverPort);
            NetworkManager.Singleton.StartServer();
            Debug.Log("Server Starting");
            NetworkManager.Singleton.OnClientDisconnectCallback += ClientDisconnected;
        }

        private void Update()
        {
            if (IsServer && _serverQueryHandler != null)
                _serverQueryHandler.UpdateServerCheck();
        }


        async Task StartServerServices()
        {
            await UnityServices.InitializeAsync();
            try
            {
                _multiplayService = MultiplayService.Instance;
                _serverQueryHandler = await _multiplayService.StartServerQueryHandlerAsync((ushort)MAXPLAYERS, "n/a", "n/a", "0", "n/a");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Something went wrong trying to set up the SQP service:\n{ex}");
            }

            try
            {
                _matchmakerPayload = await GetMatchmakerPayload(MultiplayServiceTimeout);
                if (_matchmakerPayload != null)
                {
                    Debug.Log($"Got payload: {_matchmakerPayload}");
                    await StartBackfill(_matchmakerPayload);
                }
                else
                {
                    Debug.LogWarning("Getting the Matchmaker Payload timed out, starting with defaults");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Something went wrong trying to set up the Allocation & Backfill services:\n{ex}");
            }
        }

        private void ClientDisconnected(ulong clientId)
        {
            if (!_backfilling && NetworkManager.Singleton.ConnectedClients.Count > 0 && NeedsPlayers())
            {
                BeginBackfilling(_matchmakerPayload);
            }
        }

        private async Task StartBackfill(MatchmakingResults payload)
        {
            var backFillProperties = new BackfillTicketProperties(payload.MatchProperties);
            _localBackfillTicket = new BackfillTicket { Id = payload.MatchProperties.BackfillTicketId, Properties = backFillProperties };
            await BeginBackfilling(payload);
        }

        private async Task BeginBackfilling(MatchmakingResults payload)
        {
            if (string.IsNullOrEmpty(_localBackfillTicket.Id))
            {
                var matchProperties = payload.MatchProperties;

                _createBackfillTicketOptions = new CreateBackfillTicketOptions
                {
                    Connection = ExternalConnectionString,
                    QueueName = payload.QueueName,
                    Properties = new BackfillTicketProperties(matchProperties)
                };

                _localBackfillTicket.Id =
                    await MatchmakerService.Instance.CreateBackfillTicketAsync(_createBackfillTicketOptions);
            }

            _backfilling = true;
#pragma warning disable 4014
            BackfillLoop();
#pragma warning restore 4014
        }

        private async Task BackfillLoop()
        {
            while (_backfilling && NeedsPlayers())
            {
                _localBackfillTicket =
                    await MatchmakerService.Instance.ApproveBackfillTicketAsync(_localBackfillTicket.Id);

                if (!NeedsPlayers())
                {
                    await MatchmakerService.Instance.DeleteBackfillTicketAsync(_localBackfillTicket.Id);
                    _localBackfillTicket.Id = null;
                    _backfilling = false;
                    return;
                }

                await Task.Delay(_ticketCheckMs);
            }

            _backfilling = false;
        }

        private async Task<MatchmakingResults> GetMatchmakerPayload(int timeout)
        {
            var matchmakerPayloadTask = SubscribeAndAwaitMatchmakerAllocation();
            if (await Task.WhenAny(matchmakerPayloadTask, Task.Delay(timeout)) == matchmakerPayloadTask)
            {
                return matchmakerPayloadTask.Result;
            }

            return null;
        }

        private async Task<MatchmakingResults> SubscribeAndAwaitMatchmakerAllocation()
        {
            if (_multiplayService == null) return null;
            _allocationId = null;
            _serverEventCallbacks = new MultiplayEventCallbacks();
            _serverEventCallbacks.Allocate += OnMultiplayAllocation;
            await _multiplayService.SubscribeToServerEventsAsync(_serverEventCallbacks);

            _allocationId = await AwaitAllocationId();
            var mmPayload = await GetMatchmakerAllocationPayloadAsync();
            return mmPayload;
        }

        private async Task<string> AwaitAllocationId()
        {
            var config = _multiplayService.ServerConfig;
            Debug.Log("Awaiting allocation. Server config is: \n"
                      + $"-ServerID: {config.ServerId}\n "
                      + $"-AllocationID: {config.AllocationId}\n"
                      + $"-Port: {config.Port}\n"
                      + $"-QPort: {config.QueryPort}"
                      + $"-logs: {config.ServerLogDirectory}");

            while (string.IsNullOrEmpty(_allocationId))
            {
                var configId = config.AllocationId;
                if (string.IsNullOrEmpty(configId) && string.IsNullOrEmpty(_allocationId))
                {
                    _allocationId = configId;
                    break;
                }

                await Task.Delay(100);
            }

            return _allocationId;
        }

        private void OnMultiplayAllocation(MultiplayAllocation allocation)
        {
            Debug.Log($"OnAllocation: {allocation.AllocationId}");
            if (string.IsNullOrEmpty(allocation.AllocationId)) return;
            _allocationId = allocation.AllocationId;
        }

        private async Task<MatchmakingResults> GetMatchmakerAllocationPayloadAsync()
        {
            try
            {
                var payloadAllocation =
                    await MultiplayService.Instance.GetPayloadAllocationFromJsonAs<MatchmakingResults>();
                var modelAsJson = JsonConvert.SerializeObject(payloadAllocation, Formatting.Indented);
                Debug.Log($"{nameof(GetMatchmakerAllocationPayloadAsync)}:\n {modelAsJson}");
                return payloadAllocation;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Something went wrong trying to get the Matchmaker Payload in GetMatchmakerAllocationPayloadAsync:\n{ex}");
            }
            // No payload found
            return null;
        }

        private bool NeedsPlayers() => NetworkManager.Singleton.ConnectedClients.Count < MAXPLAYERS;

        private void Dispose()
        {
            _serverEventCallbacks.Allocate -= OnMultiplayAllocation;
            _serverEvents.UnsubscribeAsync();
        }
    }
}
