using Chopper.Simulation.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Chopper.Simulation.Match
{
    /// <summary>
    /// Server only. Capture the Nuke (<see cref="MatchMode.CaptureTheNuke"/>): gathers this tick's facts (who holds the
    /// nuke, which base zone it is in, enemies in that zone), runs <see cref="NukeRules.Tick"/> and moves the nuke:
    /// on the pad, under its carrier, or dropped on the ground below where it was let go. A detonation makes a
    /// <see cref="NukeRoundWon"/> (the score system counts rounds as team scores) and puts a new nuke on the pad.
    /// Hook: a live team chopper must stay in reach of a free nuke for its <see cref="NukeHookConfig"/> time.
    /// In other modes, and between matches, the nuke waits on the pad.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(MatchScoreSystem))]
    public partial struct NukeSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NukeState>();
            state.RequireForUpdate<NukePad>();
            state.RequireForUpdate<MatchState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var match = SystemAPI.GetSingleton<MatchState>();
            var pad = SystemAPI.GetSingleton<NukePad>();
            var deltaTime = SystemAPI.Time.DeltaTime;
            var tuning = NukeTuning.Default;
            var hasPhysics = SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics);

            var bases = new NativeList<NukeBase>(4, Allocator.Temp);
            foreach (var zone in SystemAPI.Query<RefRO<NukeBase>>()) bases.Add(zone.ValueRO);

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (nukeState, carrier, transform, nuke) in
                     SystemAPI.Query<RefRW<NukeState>, RefRW<NukeCarrier>, RefRW<LocalTransform>>().WithEntityAccess())
            {
                ref var s = ref nukeState.ValueRW;
                if (match.Mode != MatchMode.CaptureTheNuke || match.Phase != MatchPhase.InProgress)
                {
                    // Not playing: a fresh nuke on the pad.
                    s = NukeRules.AtSpawn;
                    carrier.ValueRW = default;
                    transform.ValueRW.Position = NukeZones.Resting(pad.Position);
                    continue;
                }

                var position = transform.ValueRO.Position;

                // Carrier still alive? Else nobody holds it (dropped).
                var holder = carrier.ValueRO.Chopper;
                if (holder != Entity.Null && !Alive(ref state, holder)) holder = Entity.Null;

                // A free nuke: the nearest live team chopper in its hook's reach must stay there for its hook time
                // (10 s; the Lifter's winch: 5 s, from further away).
                s.HookTeam = TeamRules.None;
                if (holder == Entity.Null && s.Phase != NukePhase.Detonated)
                {
                    var candidate = Entity.Null;
                    var candidateSeconds = NukeHookConfig.BaseSeconds;
                    var best = float.MaxValue;
                    foreach (var (pose, health, team, chopper) in
                             SystemAPI.Query<RefRO<LocalToWorld>, RefRO<Health>, RefRO<Team>>().WithAll<Targetable>().WithEntityAccess())
                    {
                        if (health.ValueRO.IsDead || team.ValueRO.Value == TeamRules.None) continue;
                        var hook = SystemAPI.HasComponent<NukeHookConfig>(chopper) ? SystemAPI.GetComponent<NukeHookConfig>(chopper) : NukeHookConfig.Base;
                        var d = math.distance(pose.ValueRO.Position, position);
                        if (d > hook.Range || d >= best) continue;
                        best = d;
                        candidate = chopper;
                        candidateSeconds = hook.Seconds;
                    }

                    var hooking = carrier.ValueRO.Hooking;
                    var progress = s.HookProgress;
                    if (NukeRules.TickHook(ref progress, ref hooking, candidate, candidateSeconds, deltaTime)) holder = hooking;
                    s.HookProgress = progress;
                    s.HookTeam = hooking != Entity.Null && holder == Entity.Null ? SystemAPI.GetComponent<Team>(hooking).Value : TeamRules.None;
                    carrier.ValueRW.Hooking = holder == Entity.Null ? hooking : Entity.Null;
                }
                carrier.ValueRW.Chopper = holder;

                var carrierTeam = holder != Entity.Null ? SystemAPI.GetComponent<Team>(holder).Value : TeamRules.None;
                if (holder != Entity.Null)
                    position = NukeZones.HangPoint(SystemAPI.GetComponent<LocalToWorld>(holder).Position);

                var zoneTeam = NukeZones.ZoneOf(position, bases.AsArray());
                var input = new NukeInputs
                {
                    CarrierTeam = carrierTeam,
                    ZoneTeam = zoneTeam,
                    ZoneContested = zoneTeam != TeamRules.None && EnemyIn(ref state, bases.AsArray(), zoneTeam),
                };

                var happened = NukeRules.Tick(ref s, input, tuning, deltaTime);
                switch (happened)
                {
                    case NukeEvent.Dropped:
                        position = NukeZones.Resting(OnGround(hasPhysics, physics, position, nuke));
                        break;
                    case NukeEvent.Reset:
                        position = NukeZones.Resting(pad.Position);
                        break;
                    case NukeEvent.Detonated:
                        var won = ecb.CreateEntity();
                        ecb.AddComponent(won, new NukeRoundWon { Team = s.DetonatedBy });
                        ecb.AddComponent<NukeRoundStart>(ecb.CreateEntity());
                        UnityEngine.Debug.Log($"[Nuke] Team {s.DetonatedBy} set off the nuke in team {zoneTeam}'s base: round won.");
                        // Next round: a new nuke on the pad.
                        s = NukeRules.AtSpawn;
                        carrier.ValueRW = default;
                        position = NukeZones.Resting(pad.Position);
                        break;
                    case NukeEvent.None:
                        break;
                    default:
                        UnityEngine.Debug.Log($"[Nuke] {happened} (carrier team {carrierTeam}, zone {zoneTeam}, armed by {s.ArmedBy}).");
                        break;
                }

                transform.ValueRW.Position = position;
            }

            ecb.Playback(state.EntityManager);
            bases.Dispose();
        }

        private bool Alive(ref SystemState state, Entity chopper) =>
            SystemAPI.Exists(chopper) && SystemAPI.HasComponent<Health>(chopper) && !SystemAPI.GetComponent<Health>(chopper).IsDead;

        /// <summary>A live chopper of another team than <paramref name="zoneTeam"/> inside that team's zone.</summary>
        private bool EnemyIn(ref SystemState state, NativeArray<NukeBase> bases, byte zoneTeam)
        {
            foreach (var (pose, health, team) in SystemAPI.Query<RefRO<LocalToWorld>, RefRO<Health>, RefRO<Team>>().WithAll<Targetable>())
            {
                if (health.ValueRO.IsDead || team.ValueRO.Value == zoneTeam || team.ValueRO.Value == TeamRules.None) continue;
                if (NukeZones.ZoneOf(pose.ValueRO.Position, bases) == zoneTeam) return true;
            }
            return false;
        }

        /// <summary>Straight down to the first static surface (the nuke drops where it was let go).</summary>
        private static float3 OnGround(bool hasPhysics, in PhysicsWorldSingleton physics, float3 from, Entity self)
        {
            if (!hasPhysics) return from;
            var ray = new RaycastInput { Start = from, End = from - new float3(0f, 1000f, 0f), Filter = CollisionFilter.Default };
            var hits = new NativeList<RaycastHit>(8, Allocator.Temp);
            var best = from;
            var bestFraction = 2f;
            if (physics.CollisionWorld.CastRay(ray, ref hits))
                foreach (var hit in hits)
                {
                    // Bodies: dynamic first, then static.
                    if (hit.Entity == self || hit.RigidBodyIndex < physics.CollisionWorld.NumDynamicBodies || hit.Fraction >= bestFraction) continue;
                    bestFraction = hit.Fraction;
                    best = hit.Position;
                }
            hits.Dispose();
            return best;
        }
    }
}
