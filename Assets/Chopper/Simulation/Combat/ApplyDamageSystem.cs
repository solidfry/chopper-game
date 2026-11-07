using Chopper.Simulation.Match;
using Chopper.Simulation.Session;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Server only. Applies this tick's <see cref="DamageEvent"/>s to <see cref="Health"/>. Tells each shooter that it
    /// hit (<see cref="HitConfirmRpc"/>), each victim where the hit came from (<see cref="DamageTakenRpc"/>), and
    /// reports kills (<see cref="KillEvent"/>), and tells every client where each victim was hit
    /// (<see cref="ChopperHitRpc"/>: the strongest hit with a point, at most one per victim per tick). No damage after
    /// the match ends. Damage stops a field repair in progress and locks it (<see cref="Repair.Interrupt"/>). Pilot traits
    /// (<see cref="PilotTraits"/>): damage taken scales, gun and blast hits start damage over time, kills heal, hits jam.
    /// Death is handled by <see cref="ChopperRespawnSystem"/>.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(HitscanFireSystem))]
    [UpdateAfter(typeof(RocketSystem))]
    // Set here, not on the missile system: that one also runs on clients, where this system does not exist.
    [UpdateAfter(typeof(GuidedMissileSystem))]
    public partial struct ApplyDamageSystem : ISystem
    {
        private struct KillHeal
        {
            public Entity Healer;
            public float Share;
        }

        private ComponentLookup<ConnectionOwner> _connectionLookup;
        private ComponentLookup<GhostOwner> _ownerLookup;
        private ComponentLookup<LastAttacker> _lastAttackerLookup;
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<GhostInstance> _ghostLookup;
        private ComponentLookup<ChopperRepair> _repairLookup;
        private ComponentLookup<RepairConfig> _repairConfigLookup;
        private ComponentLookup<PilotTraits> _traitsLookup;
        private BufferLookup<DamageOverTime> _dotLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _connectionLookup = state.GetComponentLookup<ConnectionOwner>(isReadOnly: true);
            _ownerLookup = state.GetComponentLookup<GhostOwner>(isReadOnly: true);
            _lastAttackerLookup = state.GetComponentLookup<LastAttacker>();
            _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
            _ghostLookup = state.GetComponentLookup<GhostInstance>(isReadOnly: true);
            _repairLookup = state.GetComponentLookup<ChopperRepair>();
            _repairConfigLookup = state.GetComponentLookup<RepairConfig>(isReadOnly: true);
            _traitsLookup = state.GetComponentLookup<PilotTraits>(isReadOnly: true);
            _dotLookup = state.GetBufferLookup<DamageOverTime>();
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
            _ghostLookup.Update(ref state);
            _repairLookup.Update(ref state);
            _repairConfigLookup.Update(ref state);
            _traitsLookup.Update(ref state);
            _dotLookup.Update(ref state);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            // Kill heals (SCRAPPER) wait until the loop ends: the healer's health may be in the query too.
            var killHeals = new NativeList<KillHeal>(4, Allocator.Temp);

            foreach (var (health, damage, victim) in SystemAPI.Query<RefRW<Health>, DynamicBuffer<DamageEvent>>().WithEntityAccess())
            {
                if (matchOver)
                {
                    damage.Clear();
                    continue;
                }

                var strongest = new DamageEvent();
                var victimTraits = _traitsLookup.TryGetComponent(victim, out var own) ? own : PilotTraits.None;
                foreach (var raw in damage)
                {
                    if (health.ValueRO.IsDead) break;
                    // Pilots: the victim's damage taken (BULWARK, crash resistance); the attacker's damage over time
                    // (VIPER, PYRE) and HUD jam (HEX).
                    var hit = raw;
                    hit.Amount = victimTraits.Taken(raw.Amount, raw.Kind);
                    var sourceTraits = hit.Source != victim && _traitsLookup.TryGetComponent(hit.Source, out var theirs)
                        ? theirs
                        : PilotTraits.None;
                    var overTime = hit.Kind is DamageKind.Corrosion or DamageKind.Burn;
                    if (sourceTraits.DotFor(hit.Kind, hit.Amount, out var dot) && _dotLookup.HasBuffer(victim))
                    {
                        dot.Source = hit.Source;
                        DamageOverTimeRules.Add(_dotLookup[victim], dot);
                    }

                    if (hit.HasPoint && hit.Amount > strongest.Amount) strongest = hit;
                    if (hit.Amount > 0f) InterruptRepair(victim);
                    var killed = health.ValueRW.TakeDamage(hit.Amount);
                    var attacker = RememberAttacker(victim, hit.Source, now);
                    if (killed && sourceTraits.KillHeal > 0f && hit.Source != Entity.Null)
                        killHeals.Add(new KillHeal { Healer = hit.Source, Share = sourceTraits.KillHeal });

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
                            Kind = (byte)hit.Kind,
                            ImpactSpeed = hit.ImpactSpeed,
                            Jam = sourceTraits.JamOnHit && !overTime,
                        });
                        ecb.AddComponent(taken, new SendRpcCommandRequest { TargetConnection = victimConnection.Value });
                    }

                    if (!_connectionLookup.TryGetComponent(hit.Source, out var shooter)) continue;
                    // Damage over time pulses: no hit marker each half second, only the kill.
                    if (overTime && !killed) continue;
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new HitConfirmRpc { Damage = hit.Amount, Killed = killed });
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = shooter.Value });
                }
                damage.Clear();
                if (strongest.HasPoint) BroadcastHit(ecb, victim, strongest);
            }

            foreach (var heal in killHeals)
            {
                if (!SystemAPI.HasComponent<Health>(heal.Healer)) continue;
                var healer = SystemAPI.GetComponentRW<Health>(heal.Healer);
                if (healer.ValueRO.IsDead) continue;
                healer.ValueRW.Current = math.min(healer.ValueRO.Max, healer.ValueRO.Current + healer.ValueRO.Max * heal.Share);
            }
            killHeals.Dispose();
            ecb.Playback(state.EntityManager);
        }

        /// <summary>Shot, hit a wall, out of bounds: a repair in progress stops and locks for a moment.</summary>
        private void InterruptRepair(Entity victim)
        {
            if (!_repairLookup.HasComponent(victim)) return;
            var config = _repairConfigLookup.TryGetComponent(victim, out var own) ? own : RepairConfig.Default;
            var repair = _repairLookup[victim];
            if (Repair.Interrupt(ref repair, config)) _repairLookup[victim] = repair;
        }

        /// <summary>To every client: where the victim was hit, in the victim's own space.</summary>
        private void BroadcastHit(EntityCommandBuffer ecb, Entity victim, in DamageEvent hit)
        {
            if (!_ghostLookup.TryGetComponent(victim, out var ghost) || !_transformLookup.TryGetComponent(victim, out var pose)) return;
            var rpc = ecb.CreateEntity();
            ecb.AddComponent(rpc, new ChopperHitRpc
            {
                GhostId = ghost.ghostId,
                LocalPoint = pose.InverseTransformPoint(hit.Point),
                LocalNormal = pose.InverseTransformDirection(hit.Normal),
                Amount = hit.Amount,
                Kind = (byte)hit.Kind,
            });
            // No target connection: sent to every client.
            ecb.AddComponent<SendRpcCommandRequest>(rpc);
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
