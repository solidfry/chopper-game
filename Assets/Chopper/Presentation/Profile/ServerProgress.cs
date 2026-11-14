using System;
using System.Collections.Generic;
using System.Text;
using Chopper.Presentation.Results;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Match;
using Chopper.Simulation.Progression;
using Chopper.Simulation.Session;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Networking;

namespace Chopper.Presentation.Profile
{
    /// <summary>
    /// The game server's Unity service account, from the host's environment (no state). The matchmaker's allocator
    /// (Cloud Code, <c>CloudCode/ChopperAllocator</c>) puts these on the Edgegap deployment; for a test on a PC, set
    /// them by hand. Not all set = no server-side progress (local sessions, the editor).
    /// </summary>
    public readonly struct ServerCloudConfig
    {
        public readonly string ProjectId;
        public readonly string EnvironmentId;
        public readonly string KeyId;
        public readonly string SecretKey;
        /// <summary>The matchmaker's match id ("" on a server started by hand).</summary>
        public readonly string MatchId;

        private ServerCloudConfig(string projectId, string environmentId, string keyId, string secretKey, string matchId)
        {
            ProjectId = projectId;
            EnvironmentId = environmentId;
            KeyId = keyId;
            SecretKey = secretKey;
            MatchId = matchId;
        }

        public bool IsSet => !string.IsNullOrEmpty(ProjectId) && !string.IsNullOrEmpty(EnvironmentId) &&
                             !string.IsNullOrEmpty(KeyId) && !string.IsNullOrEmpty(SecretKey);

        public static ServerCloudConfig FromEnvironment() => new(
            Read("UGS_PROJECT_ID"), Read("UGS_ENVIRONMENT_ID"), Read("UGS_KEY_ID"), Read("UGS_SECRET_KEY"), Read("MATCH_ID"));

        private static string Read(string name) => (Environment.GetEnvironmentVariable(name) ?? "").Trim();
    }

    /// <summary>
    /// Calls the progress functions of the Cloud Code module as the game server: trades the service account for a
    /// short-lived token (Unity's token exchange), then posts to the module's run endpoint. One per server world.
    /// Written without the editor (14 Nov 2026); the two web addresses are from Unity's Cloud Code guide
    /// "Call from a game server".
    /// </summary>
    public sealed class CloudProgressClient
    {
        /// <summary>The module with the Progress_ functions (they live next to the allocator).</summary>
        public const string Module = "ChopperAllocator";
        private const string TokenUrl = "https://services.api.unity.com/auth/v1/token-exchange";
        private const string RunUrl = "https://cloud-code.services.api.unity.com/v1/projects";
        /// <summary>A token is asked for again after this long (it lasts longer; a refused call also asks again).</summary>
        private const double TokenSeconds = 1200.0;
        private const int TimeoutSeconds = 15;

        [Serializable] private sealed class TokenReply { public string accessToken; }
        [Serializable] private sealed class StringReply { public string output; }
        [Serializable] private sealed class ReadParams { public string playerId; }
        [Serializable] private sealed class WriteParams { public string playerId; public string matchId; public string profile; }

        private readonly ServerCloudConfig _config;
        private string _token;
        private double _tokenTime;

        public CloudProgressClient(in ServerCloudConfig config) => _config = config;

        /// <summary>The player's record as JSON text, or "" when there is none. Throws when the call fails.</summary>
        public UniTask<string> ReadAsync(string playerId) =>
            CallAsync("Progress_Read", JsonUtility.ToJson(new ReadParams { playerId = playerId }));

        /// <summary>Replaces the player's record. Throws when the call fails.</summary>
        public UniTask<string> WriteAsync(string playerId, string profileJson) =>
            CallAsync("Progress_Write", JsonUtility.ToJson(new WriteParams { playerId = playerId, matchId = _config.MatchId, profile = profileJson }));

        private async UniTask<string> CallAsync(string function, string parametersJson)
        {
            if (_token == null || Time.realtimeSinceStartupAsDouble - _tokenTime > TokenSeconds) await RefreshToken();
            var url = $"{RunUrl}/{_config.ProjectId}/modules/{Module}/{function}";
            var reply = await Post(url, "{\"params\":" + parametersJson + "}", "Bearer " + _token);
            var parsed = JsonUtility.FromJson<StringReply>(reply);
            return parsed != null ? parsed.output ?? "" : "";
        }

        private async UniTask RefreshToken()
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.KeyId}:{_config.SecretKey}"));
            var url = $"{TokenUrl}?projectId={_config.ProjectId}&environmentId={_config.EnvironmentId}";
            var reply = await Post(url, "{}", "Basic " + basic);
            var parsed = JsonUtility.FromJson<TokenReply>(reply);
            if (parsed == null || string.IsNullOrEmpty(parsed.accessToken)) throw new InvalidOperationException("The token exchange gave no token.");
            _token = parsed.accessToken;
            _tokenTime = Time.realtimeSinceStartupAsDouble;
        }

        private static async UniTask<string> Post(string url, string json, string authorization)
        {
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = TimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", authorization);
            // UniTask throws a UnityWebRequestException when the request fails.
            await request.SendWebRequest();
            return request.downloadHandler.text;
        }
    }

    /// <summary>One player's part of a finished match, as the server grants it. Pure (edit-mode tests: ProgressionTests).</summary>
    public static class MatchGrant
    {
        /// <summary>
        /// The outcome the profile records for <paramref name="playerId"/>: awards and XP by the same rules as the
        /// results screen (<see cref="MatchAwards"/>, <see cref="MatchXp"/>), the place by kills then fewest deaths.
        /// </summary>
        public static MatchOutcome Outcome(int playerId, IReadOnlyDictionary<int, PlayerMatchStats> all, int winnerId, in XpRates rates,
            string pilot, FlightShare flown)
        {
            all.TryGetValue(playerId, out var stats);
            var awards = new List<Award>();
            MatchAwards.For(playerId, all, awards);
            var won = winnerId != 0 && winnerId == playerId;
            var xp = MatchXp.Lines(stats, awards, won, new List<XpLine>(), rates);
            return new MatchOutcome(stats, awards, won, winnerId == 0, Place(playerId, all), all.Count, xp, MatchResultsModel.ScoreOf(stats),
                pilot, new[] { flown });
        }

        /// <summary>1 = first. Players with the same kills and deaths share a place.</summary>
        public static int Place(int playerId, IReadOnlyDictionary<int, PlayerMatchStats> all)
        {
            if (!all.TryGetValue(playerId, out var own)) return all.Count;
            var better = 0;
            foreach (var other in all.Values)
                if (other.Kills > own.Kills || (other.Kills == own.Kills && other.Deaths < own.Deaths)) better++;
            return better + 1;
        }
    }

    /// <summary>
    /// Dedicated or hosted server with a service account (<see cref="ServerCloudConfig"/>); off without one. Plan
    /// section 6a-1, step 4.
    /// - A player joins: reads their record from Cloud Save and puts the real XP on the connection
    ///   (<see cref="PlayerProgress"/>, Trusted). <see cref="LoadoutGateSystem"/> waits a few seconds for it.
    /// - A match ends: works out each player's awards and XP (the game's own rules), adds the match to their record
    ///   and writes it back. Bots and players without an account id get nothing.
    /// Lives in the presentation assembly because the award, XP and profile rules do. Written without the editor.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class ServerProgressSystem : SystemBase
    {
        /// <summary>Seconds after the match ends before the grant: the last kill's stats are in by then.</summary>
        private const double GrantDelaySeconds = 1.0;

        private readonly Dictionary<string, PlayerProfile> _profiles = new();
        private readonly HashSet<Entity> _asked = new();
        private CloudProgressClient _client;
        private EntityQuery _joining, _players, _catalog, _rules, _match;
        private MatchPhase _phase;
        private double _grantAt = -1.0;

        protected override void OnCreate()
        {
            var config = ServerCloudConfig.FromEnvironment();
            if (!config.IsSet)
            {
                Enabled = false;
                return;
            }
            _client = new CloudProgressClient(config);
            _joining = GetEntityQuery(ComponentType.ReadOnly<PlayerAccount>(), ComponentType.ReadOnly<PlayerProgress>(),
                ComponentType.ReadOnly<LoadoutPending>());
            _players = GetEntityQuery(ComponentType.ReadOnly<PlayerId>(), ComponentType.ReadOnly<PlayerStats>());
            _catalog = GetEntityQuery(ComponentType.ReadOnly<LoadoutCatalogRef>());
            _rules = GetEntityQuery(ComponentType.ReadOnly<ProgressionRules>());
            _match = GetEntityQuery(ComponentType.ReadOnly<MatchState>());
            EntityManager.CreateSingleton<CloudProgressOn>("CloudProgressOn");
            Debug.Log("[Progress] Service account found: player progress is read from and written to Cloud Save.");
        }

        protected override void OnUpdate()
        {
            var joining = _joining.ToEntityArray(Allocator.Temp);
            foreach (var connection in joining)
            {
                if (!_asked.Add(connection)) continue;
                var account = EntityManager.GetComponentData<PlayerAccount>(connection).Id.ToString();
                if (account.Length > 0) Fetch(connection, account).Forget();
            }
            joining.Dispose();

            var now = SystemAPI.Time.ElapsedTime;
            var phase = _match.TryGetSingleton(out MatchState match) ? match.Phase : MatchPhase.InProgress;
            if (phase == MatchPhase.PostGame && _phase != MatchPhase.PostGame) _grantAt = now + GrantDelaySeconds;
            if (phase == MatchPhase.InProgress) _grantAt = -1.0;
            _phase = phase;
            if (_grantAt >= 0.0 && now >= _grantAt)
            {
                _grantAt = -1.0;
                Grant(match.WinnerNetworkId);
            }
        }

        private LoadoutCatalog Catalog => _catalog.TryGetSingleton(out LoadoutCatalogRef reference) ? reference.Catalog.Value : null;

        /// <summary>The chassis class and pilot of the loadout the player flies (after the gate).</summary>
        private FlightShare Flown(Entity connection, out string pilotName)
        {
            pilotName = "";
            var catalog = Catalog;
            if (catalog == null || !EntityManager.HasComponent<RequestedLoadout>(connection)) return default;
            var code = EntityManager.GetComponentData<RequestedLoadout>(connection).Code;
            var chassisClass = catalog.TryGetChassis(code.ChassisId, out var chassis) ? chassis.Class : ChassisClass.None;
            var pilot = catalog.FindPilot(code.PilotId);
            if (pilot != null) pilotName = pilot.CallSign ?? "";
            return new FlightShare(chassisClass, pilot != null ? code.PilotId : (ushort)0, 1f);
        }

        private async UniTaskVoid Fetch(Entity connection, string account)
        {
            try
            {
                var json = await _client.ReadAsync(account);
                var profile = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PlayerProfile>(json);
                profile ??= new PlayerProfile();
                _profiles[account] = profile;

                // The world can be gone, or the player can have left, while the call ran.
                if (!World.IsCreated || !EntityManager.Exists(connection) || !EntityManager.HasComponent<PlayerProgress>(connection)) return;
                var flown = Flown(connection, out _);
                var progress = EntityManager.GetComponentData<PlayerProgress>(connection);
                progress.AccountXp = profile.totalXp;
                progress.ChassisXp = profile.ChassisXpOf((int)flown.Class);
                progress.PilotXp = profile.PilotXpOf(flown.PilotId);
                progress.Trusted = true;
                EntityManager.SetComponentData(connection, progress);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Progress] Could not read the record of {account}: {e.Message}. The client's own numbers are used.");
            }
        }

        private void Grant(int winnerId)
        {
            var all = new Dictionary<int, PlayerMatchStats>();
            var connections = _players.ToEntityArray(Allocator.Temp);
            foreach (var connection in connections)
                all[EntityManager.GetComponentData<PlayerId>(connection).Value] = EntityManager.GetComponentData<PlayerStats>(connection).Value;

            var rates = _rules.TryGetSingleton(out ProgressionRules rules) ? rules.Xp : XpRates.Default;
            var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var granted = 0;
            foreach (var connection in connections)
            {
                if (!EntityManager.HasComponent<PlayerAccount>(connection)) continue;
                var account = EntityManager.GetComponentData<PlayerAccount>(connection).Id.ToString();
                if (account.Length == 0) continue;

                // No record in memory: the read failed. Do not write, or a new record would replace the real one.
                if (!_profiles.TryGetValue(account, out var profile))
                {
                    Debug.LogWarning($"[Progress] No record was read for {account}: this match is not granted.");
                    continue;
                }
                var id = EntityManager.GetComponentData<PlayerId>(connection).Value;
                var flown = Flown(connection, out var pilotName);
                ProfileRules.Record(profile, MatchGrant.Outcome(id, all, winnerId, rates, pilotName, flown), time);
                Write(account, JsonUtility.ToJson(profile)).Forget();
                granted++;
            }
            connections.Dispose();
            Debug.Log($"[Progress] Match end: {granted} player record(s) sent to Cloud Save.");
        }

        private async UniTaskVoid Write(string account, string json)
        {
            try
            {
                await _client.WriteAsync(account, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Progress] Could not write the record of {account}: {e.Message}");
            }
        }
    }
}
