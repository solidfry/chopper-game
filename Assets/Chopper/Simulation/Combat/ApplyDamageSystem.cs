using Chopper.Simulation.Match;
using Chopper.Simulation.Session;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Server only. Applies this tick's <see cref="DamageEvent"/>s to <see cref="Health"/>. Tells each shooter that it
    /// hit (<see cref="HitConfirmRpc"/>), each victim where the hit came from (<see cref="DamageTakenRpc"/>), and
    /// reports kills (<see cref="KillEvent"/>). No damage after the match ends.
    /// Death is handled by <see cref="ChopperRespawnSystem"/>.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(HitscanFireSystem))]
    [UpdateAfter(typeof(RocketSystem))]
    public partial struct ApplyDamageSystem : ISystem
    {
        private ComponentLookup<ConnectionOwner> _connectionLookup;
        private ComponentLookup<GhostOwner> _ownerLookup;
        private ComponentLookup<LastAttacker> _lastAttackerLookup;
        private ComponentLookup<LocalTransform> _transformLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _connectionLookup = state.GetComponentLookup<ConnectionOwner>(isReadOnly: true);
            _ownerLookup = state.GetComponentLookup<GhostOwner>(isReadOnly: true);
            _lastAttackerLookup = state.GetComponentLookup<LastAttacker>();
            _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var matchOver = SystemAPI.TryGetSingleton<MatchState>(out var match) && match.Phase != MatchPhase.InProgress;
            var rules = SystemAPI.TryGetSingleton<MatchRules>(out var baked) ? baked : MatchRules.Default;
            var now = SystemAPI.Time.ElapsedTime;

            _connectionLookup.Update(ref state);
            _ownerLookup.Update(ref state);
            _lastAttackerLookup.Update(ref state);
            _transformLookup.Update(ref state);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (health, damage, victim) in SystemAPI.Query<RefRW<Health>, DynamicBuffer<DamageEvent>>().WithEntityAccess())
            {
                if (matchOver)
                {
                    damage.Clear();
                    continue;
                }

                foreach (var hit in damage)
                {
                    if (health.ValueRO.IsDead) break;
                    var killed = health.ValueRW.TakeDamage(hit.Amount);
                    var attacker = RememberAttacker(victim, hit.Source, now);

                    if (killed && _ownerLookup.TryGetComponent(victim, out var victimOwner))
                    {
                        var killer = attacker != 0 ? attacker : RecentAttacker(victim, now, rules.KillCreditSeconds);
                        var kill = ecb.CreateEntity();
                        ecb.AddComponent(kill, new KillEvent { KillerNetworkId = killer, VictimNetworkId = victimOwner.NetworkId });
                    }

                    if (_connectionLookup.TryGetComponent(victim, out var victimConnection))
                    {
                        var hasSource = _transformLookup.TryGetComponent(hit.Source, out var sourcePose);
                        var taken = ecb.CreateEntity();
                        ecb.AddComponent(taken, new DamageTakenRpc
                        {
                            Amount = hit.Amount,
                            SourcePosition = sourcePose.Position,
                            HasSource = hasSource,
                        });
                        ecb.AddComponent(taken, new SendRpcCommandRequest { TargetConnection = victimConnection.Value });
                    }

                    if (!_connectionLookup.TryGetComponent(hit.Source, out var shooter)) continue;
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new HitConfirmRpc { Damage = hit.Amount, Killed = killed });
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = shooter.Value });
                }
                damage.Clear();
            }
            ecb.Playback(state.EntityManager);
        }

        /// <summary>Returns the attacker's network id, or 0 for environment damage and self damage.</summary>
        private int RememberAttacker(Entity victim, Entity source, double now)
        {
            if (source == victim || !_ownerLookup.TryGetComponent(source, out var owner)) return 0;
            if (_lastAttackerLookup.HasComponent(victim))
                _lastAttackerLookup[victim] = new LastAttacker { NetworkId = owner.NetworkId, Time = now };
            return owner.NetworkId;
        }

        private int RecentAttacker(Entity victim, double now, float creditSeconds)
        {
            return _lastAttackerLookup.TryGetComponent(victim, out var last) && last.NetworkId != 0 && now - last.Time <= creditSeconds
                ? last.NetworkId
                : 0;
        }
    }
}
