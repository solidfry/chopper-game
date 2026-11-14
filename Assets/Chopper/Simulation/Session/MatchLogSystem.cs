using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Match;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// Server only: the match log, for later analysis (balance, map flow, server load). One JSON object per line:
    /// to the server log with the prefix <see cref="LinePrefix"/> (Edgegap container logs; <c>Tools/Edgegap/fetch-logs.ps1</c>
    /// saves them) and to a file (<c>-matchLogDir PATH</c>, else <c>persistentDataPath/MatchLogs</c>).
    /// On in a dedicated server; elsewhere with <c>-matchLog</c>.
    /// Every line has <c>t</c> (server seconds since start, 3 decimals) and <c>e</c> (event). Events:
    /// <list type="bullet">
    /// <item><c>session</c>: build version, Edgegap request id and location, tick rate, UTC start, log format version.</item>
    /// <item><c>map</c>: map id and scene. <c>match</c>: phase change (start / end with winner and scores).</item>
    /// <item><c>join</c> / <c>leave</c>: player (network id) and the loadout asked for (chassis, modules, pilot, skin).</item>
    /// <item><c>spawn</c>: a chopper appeared (position, chassis). <c>kill</c>: killer (0 = none), victim, weapon,
    /// damage kind, distance, both positions, victim's life length.</item>
    /// <item><c>shots</c> and <c>hits</c>: summed per second per (player, weapon) and (player, target, weapon, kind):
    /// count, damage, mean distance. Crashes, bounds and damage over time are hits with actor 0 or the attacker.</item>
    /// <item><c>flare</c>, <c>decoy</c> (a heat seeker lost its target), <c>missile</c> (end: impact, proximity, expired),
    /// <c>repair</c> (health mended by one field repair), <c>ability</c> (pilot ability started).</item>
    /// <item><c>pos</c>: every second, each chopper as [id, x, y, z, speed, health].</item>
    /// <item><c>load</c>: from <see cref="LoadLogSystem"/>.</item>
    /// </list>
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial class MatchLogSystem : SystemBase
    {
        public const string LinePrefix = "[Event] ";
        /// <summary>Raise when a field changes meaning or is removed, so analysis scripts can tell old logs apart.</summary>
        public const int FormatVersion = 1;
        private const double SampleSeconds = 1.0;

        private struct ShotKey : IEquatable<ShotKey>
        {
            public int Actor;
            public FixedString32Bytes Weapon;
            public bool Equals(ShotKey other) => Actor == other.Actor && Weapon.Equals(other.Weapon);
            public override int GetHashCode() => Actor * 397 ^ Weapon.GetHashCode();
        }

        private struct HitKey : IEquatable<HitKey>
        {
            public int Actor;
            public int Target;
            public FixedString32Bytes Weapon;
            public DamageKind Kind;
            public bool Equals(HitKey other) =>
                Actor == other.Actor && Target == other.Target && Kind == other.Kind && Weapon.Equals(other.Weapon);
            public override int GetHashCode() => ((Actor * 397 ^ Target) * 397 ^ (int)Kind) * 397 ^ Weapon.GetHashCode();
        }

        private struct HitSum
        {
            public int Count;
            public float Damage;
            public float DistanceSum;
            public int DistanceCount;
            public int Kills;
        }

        private readonly StringBuilder _line = new StringBuilder(256);
        private readonly Dictionary<ShotKey, int> _shots = new Dictionary<ShotKey, int>();
        private readonly Dictionary<HitKey, HitSum> _hits = new Dictionary<HitKey, HitSum>();
        private readonly Dictionary<int, double> _alive = new Dictionary<int, double>();
        private readonly HashSet<int> _players = new HashSet<int>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _gone = new List<int>();

        private bool _toConsole;
        private StreamWriter _file;
        private double _nextSample;
        private bool _sessionWritten;
        private int _mapId = -1;
        private MatchPhase? _phase;

        /// <summary>Where this server's log file is (null when there is no file).</summary>
        public string FilePath { get; private set; }

        protected override void OnCreate()
        {
#if UNITY_SERVER
            const bool dedicated = true;
#else
            var dedicated = Application.isBatchMode;
#endif
            if (!dedicated && !ServerArgs.Flag("-matchLog"))
            {
                Enabled = false;
                return;
            }
            _toConsole = true;
            // The buffer itself belongs to MatchStatsSystem, which also clears it after this system.
            OpenFile();
        }

        protected override void OnDestroy()
        {
            Flush();
            _file?.Dispose();
            _file = null;
        }

        protected override void OnUpdate()
        {
            var now = SystemAPI.Time.ElapsedTime;
            if (!_sessionWritten) WriteSession(now);
            WriteMap(now);
            WriteMatch(now);
            // Kills first: the respawn system already removed the victim's chopper, and its spawn time is still kept.
            Drain(now);
            WritePlayers(now);
            WriteSpawns(now);
            if (now >= _nextSample)
            {
                _nextSample = now + SampleSeconds;
                WriteSums(now);
                WritePositions(now);
                _file?.Flush();
            }
        }

        /// <summary>Writes one event: <paramref name="fields"/> is the rest of the JSON object (<c>"a":1,"b":2</c>), or empty.</summary>
        public void Write(string eventName, string fields)
        {
            if (!Enabled) return;
            Begin(World.Time.ElapsedTime, eventName);
            if (!string.IsNullOrEmpty(fields)) _line.Append(',').Append(fields);
            End();
        }

        private void OpenFile()
        {
            try
            {
                var folder = ServerArgs.String("-matchLogDir") ?? Path.Combine(Application.persistentDataPath, "MatchLogs");
                Directory.CreateDirectory(folder);
                var request = Environment.GetEnvironmentVariable("ARBITRIUM_REQUEST_ID");
                var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}_{(string.IsNullOrEmpty(request) ? "local" : request)}.jsonl";
                FilePath = Path.Combine(folder, name);
                _file = new StreamWriter(FilePath, append: false, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Server] Match log file not opened ({e.Message}); the log goes to the console only.");
                _file = null;
                FilePath = null;
            }
        }

        private void WriteSession(double now)
        {
            _sessionWritten = true;
            var tickRate = SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rates) ? rates.SimulationTickRate : 0;
            Begin(now, "session");
            Field("format", FormatVersion);
            Field("utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            Field("version", Application.version);
            Field("unity", Application.unityVersion);
            Field("request", Environment.GetEnvironmentVariable("ARBITRIUM_REQUEST_ID") ?? "");
            Field("city", Environment.GetEnvironmentVariable("ARBITRIUM_DEPLOYMENT_LOCATION_CITY") ?? "");
            Field("country", Environment.GetEnvironmentVariable("ARBITRIUM_DEPLOYMENT_LOCATION_COUNTRY") ?? "");
            Field("tickRate", tickRate);
            Field("cpus", SystemInfo.processorCount);
            End();
        }

        private void WriteMap(double now)
        {
            if (!SystemAPI.TryGetSingleton<MatchMap>(out var map) || map.MapId == _mapId) return;
            _mapId = map.MapId;
            Begin(now, "map");
            Field("id", map.MapId);
            Field("scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            End();
        }

        private void WriteMatch(double now)
        {
            if (!SystemAPI.TryGetSingleton<MatchState>(out var match) || match.Phase == _phase) return;
            _phase = match.Phase;
            Begin(now, "match");
            Field("phase", match.Phase == MatchPhase.InProgress ? "start" : "end");
            Field("timeLeft", match.TimeRemaining);
            if (match.Phase != MatchPhase.InProgress)
            {
                Field("winner", match.WinnerNetworkId);
                _line.Append(",\"scores\":[");
                var first = true;
                foreach (var (score, id) in SystemAPI.Query<RefRO<PlayerScore>, RefRO<PlayerId>>())
                {
                    if (!first) _line.Append(',');
                    first = false;
                    _line.Append('[').Append(id.ValueRO.Value).Append(',').Append(score.ValueRO.Kills).Append(',')
                        .Append(score.ValueRO.Deaths).Append(']');
                }
                _line.Append(']');
            }
            End();
        }

        private void WritePlayers(double now)
        {
            _seen.Clear();
            foreach (var (id, connection) in SystemAPI.Query<RefRO<PlayerId>>().WithEntityAccess())
            {
                var networkId = id.ValueRO.Value;
                _seen.Add(networkId);
                if (_players.Contains(networkId)) continue;
                // Wait for the loadout the client asked for (it comes with the go-in-game request).
                if (!SystemAPI.HasComponent<RequestedLoadout>(connection)) continue;
                _players.Add(networkId);
                var requested = SystemAPI.GetComponent<RequestedLoadout>(connection);
                Begin(now, "join");
                Field("id", networkId);
                Loadout(requested.Code);
                Field("skin", requested.SkinId);
                End();
            }

            _gone.Clear();
            foreach (var id in _players)
                if (!_seen.Contains(id)) _gone.Add(id);
            foreach (var id in _gone)
            {
                _players.Remove(id);
                Begin(now, "leave");
                Field("id", id);
                End();
            }
        }

        private void WriteSpawns(double now)
        {
            _seen.Clear();
            foreach (var (owner, pose, loadout) in SystemAPI.Query<RefRO<GhostOwner>, RefRO<LocalTransform>, RefRO<ChopperLoadout>>()
                         .WithAll<ChopperFlightConfig>())
            {
                var id = owner.ValueRO.NetworkId;
                _seen.Add(id);
                if (_alive.ContainsKey(id)) continue;
                _alive[id] = now;
                Begin(now, "spawn");
                Field("id", id);
                Position("pos", pose.ValueRO.Position);
                Loadout(loadout.ValueRO.Code);
                End();
            }
            _gone.Clear();
            foreach (var id in _alive.Keys)
                if (!_seen.Contains(id)) _gone.Add(id);
            foreach (var id in _gone) _alive.Remove(id);
        }

        private void Drain(double now)
        {
            if (!SystemAPI.TryGetSingletonBuffer<MatchLogEntry>(out var buffer) || buffer.Length == 0) return;
            foreach (var entry in buffer)
            {
                switch (entry.Kind)
                {
                    case MatchLogKind.Shot:
                    {
                        var key = new ShotKey { Actor = entry.Actor, Weapon = entry.Weapon };
                        _shots.TryGetValue(key, out var count);
                        _shots[key] = count + 1;
                        break;
                    }
                    case MatchLogKind.Hit:
                    {
                        var key = new HitKey { Actor = entry.Actor, Target = entry.Target, Weapon = entry.Weapon, Kind = entry.Damage };
                        _hits.TryGetValue(key, out var sum);
                        sum.Count++;
                        sum.Damage += entry.Amount;
                        if (entry.Distance >= 0f)
                        {
                            sum.DistanceSum += entry.Distance;
                            sum.DistanceCount++;
                        }
                        if (entry.Detail != 0) sum.Kills++;
                        _hits[key] = sum;
                        break;
                    }
                    case MatchLogKind.Kill:
                        Begin(entry.Time, "kill");
                        Field("killer", entry.Actor);
                        Field("victim", entry.Target);
                        Field("weapon", entry.Weapon.ToString());
                        Field("kind", entry.Damage.ToString());
                        Field("dist", entry.Distance);
                        Position("kpos", entry.ActorPosition);
                        Position("vpos", entry.TargetPosition);
                        if (_alive.TryGetValue(entry.Target, out var since)) Field("life", entry.Time - since);
                        End();
                        break;
                    case MatchLogKind.Flare:
                        Begin(entry.Time, "flare");
                        Field("id", entry.Actor);
                        Position("pos", entry.ActorPosition);
                        End();
                        break;
                    case MatchLogKind.MissileDecoyed:
                        Begin(entry.Time, "decoy");
                        Field("shooter", entry.Actor);
                        Field("target", entry.Target);
                        Field("weapon", entry.Weapon.ToString());
                        Field("dist", entry.Distance);
                        End();
                        break;
                    case MatchLogKind.MissileEnd:
                        Begin(entry.Time, "missile");
                        Field("shooter", entry.Actor);
                        Field("target", entry.Target);
                        Field("weapon", entry.Weapon.ToString());
                        Field("end", ((MissileOutcome)entry.Detail).ToString());
                        Field("dist", entry.Distance);
                        Position("pos", entry.ActorPosition);
                        End();
                        break;
                    case MatchLogKind.Repair:
                        Begin(entry.Time, "repair");
                        Field("id", entry.Actor);
                        Field("hp", entry.Amount);
                        End();
                        break;
                    case MatchLogKind.Ability:
                        Begin(entry.Time, "ability");
                        Field("id", entry.Actor);
                        Field("ability", ((AbilityKind)entry.Detail).ToString());
                        Position("pos", entry.ActorPosition);
                        End();
                        break;
                }
            }
        }

        private void WriteSums(double now)
        {
            foreach (var pair in _shots)
            {
                Begin(now, "shots");
                Field("id", pair.Key.Actor);
                Field("weapon", pair.Key.Weapon.ToString());
                Field("n", pair.Value);
                End();
            }
            _shots.Clear();
            foreach (var pair in _hits)
            {
                var sum = pair.Value;
                Begin(now, "hits");
                Field("id", pair.Key.Actor);
                Field("target", pair.Key.Target);
                Field("weapon", pair.Key.Weapon.ToString());
                Field("kind", pair.Key.Kind.ToString());
                Field("n", sum.Count);
                Field("dmg", sum.Damage);
                Field("dist", sum.DistanceCount > 0 ? sum.DistanceSum / sum.DistanceCount : -1f);
                if (sum.Kills > 0) Field("kills", sum.Kills);
                End();
            }
            _hits.Clear();
        }

        private void WritePositions(double now)
        {
            var any = false;
            foreach (var (owner, pose, velocity, health) in SystemAPI
                         .Query<RefRO<GhostOwner>, RefRO<LocalTransform>, RefRO<PhysicsVelocity>, RefRO<Health>>()
                         .WithAll<ChopperFlightConfig>())
            {
                if (!any) Begin(now, "pos").Append(",\"p\":[");
                else _line.Append(',');
                any = true;
                var p = pose.ValueRO.Position;
                _line.Append('[').Append(owner.ValueRO.NetworkId).Append(',');
                Number(p.x).Append(',');
                Number(p.y).Append(',');
                Number(p.z).Append(',');
                Number(math.length(velocity.ValueRO.Linear)).Append(',');
                Number(health.ValueRO.Current).Append(']');
            }
            if (!any) return;
            _line.Append(']');
            End();
        }

        private void Loadout(in LoadoutCode code)
        {
            var catalog = SystemAPI.TryGetSingleton<LoadoutCatalogRef>(out var reference) ? reference.Catalog.Value : null;
            var chassisName = "";
            var slots = 0;
            if (catalog != null && catalog.TryGetChassis(code.ChassisId, out var chassis))
            {
                chassisName = chassis.Name;
                slots = chassis.Slots.Count;
            }
            Field("chassis", chassisName.Length > 0 ? chassisName : code.ChassisId.ToString(CultureInfo.InvariantCulture));
            _line.Append(",\"modules\":[");
            for (var slot = 0; slot < slots; slot++)
            {
                if (slot > 0) _line.Append(',');
                var moduleId = code.GetModule(slot);
                var moduleName = moduleId != 0 && catalog.TryGetModule(moduleId, out var module) ? module.Name : moduleId.ToString(CultureInfo.InvariantCulture);
                Text(moduleName);
            }
            _line.Append(']');
            var pilotName = code.PilotId.ToString(CultureInfo.InvariantCulture);
            if (catalog != null)
                foreach (var pilot in catalog.Pilots)
                    if (pilot != null && pilot.Id == code.PilotId) pilotName = pilot.CallSign;
            Field("pilot", pilotName);
        }

        // JSON building: fields are appended to _line between Begin and End.

        private StringBuilder Begin(double time, string eventName)
        {
            _line.Clear();
            _line.Append("{\"t\":").Append(time.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\"e\":");
            Text(eventName);
            return _line;
        }

        private void End()
        {
            _line.Append('}');
            var text = _line.ToString();
            if (_toConsole) Debug.Log(LinePrefix + text);
            _file?.WriteLine(text);
        }

        private void Flush() => _file?.Flush();

        private void Field(string name, string value)
        {
            _line.Append(',');
            Text(name);
            _line.Append(':');
            Text(value);
        }

        private void Field(string name, int value) => _line.Append(",\"").Append(name).Append("\":").Append(value);

        private void Field(string name, double value)
        {
            _line.Append(",\"").Append(name).Append("\":");
            Number(value);
        }

        private void Position(string name, float3 p)
        {
            _line.Append(",\"").Append(name).Append("\":[");
            Number(p.x).Append(',');
            Number(p.y).Append(',');
            Number(p.z).Append(']');
        }

        private StringBuilder Number(double value) =>
            double.IsNaN(value) || double.IsInfinity(value)
                ? _line.Append("null")
                : _line.Append(value.ToString("0.##", CultureInfo.InvariantCulture));

        private void Text(string value)
        {
            _line.Append('"');
            foreach (var c in value ?? "")
            {
                switch (c)
                {
                    case '"': _line.Append("\\\""); break;
                    case '\\': _line.Append("\\\\"); break;
                    case '\n': _line.Append("\\n"); break;
                    case '\r': _line.Append("\\r"); break;
                    case '\t': _line.Append("\\t"); break;
                    default:
                        if (c < ' ') _line.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else _line.Append(c);
                        break;
                }
            }
            _line.Append('"');
        }
    }
}
