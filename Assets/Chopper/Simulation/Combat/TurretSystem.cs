using Chopper.Simulation.Match;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Server only. Turrets (<see cref="TurretConfig"/>): pick the nearest enemy chopper in range and in sight, turn the
    /// barrel to its lead point, and fire <see cref="ActiveBullet"/>s into the shared buffer (<see cref="BulletStepSystem"/>
    /// flies them, no rewind: the turret is on the server). A destroyed turret is down, then respawns at full health.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ExportPhysicsWorld))]
    [UpdateBefore(typeof(BulletStepSystem))]
    public partial struct TurretSystem : ISystem
    {
        private struct Candidate
        {
            public Entity Entity;
            public float3 Position;
            public float3 Velocity;
            public byte Team;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TurretConfig>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<ActiveBullet>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            var bullets = SystemAPI.GetSingletonBuffer<ActiveBullet>();

            // Live air targets (choppers) this tick.
            var candidates = new NativeList<Candidate>(16, Allocator.Temp);
            foreach (var (targetable, health, pose, entity) in
                     SystemAPI.Query<RefRO<Targetable>, RefRO<Health>, RefRO<LocalToWorld>>().WithEntityAccess())
            {
                if ((targetable.ValueRO.Class & TargetClass.Air) == 0 || health.ValueRO.IsDead) continue;
                candidates.Add(new Candidate
                {
                    Entity = entity,
                    Position = pose.ValueRO.Position,
                    Velocity = SystemAPI.HasComponent<PhysicsVelocity>(entity) ? SystemAPI.GetComponent<PhysicsVelocity>(entity).Linear : float3.zero,
                    Team = SystemAPI.HasComponent<Team>(entity) ? SystemAPI.GetComponent<Team>(entity).Value : (byte)0,
                });
            }

            // Pilots in the bigger team (all players when there are no teams): the turret health scale.
            int team1 = 0, team2 = 0, players = 0;
            foreach (var team in SystemAPI.Query<RefRO<Match.Team>>().WithAll<Session.PlayerId>())
            {
                players++;
                if (team.ValueRO.Value == 1) team1++;
                else if (team.ValueRO.Value == 2) team2++;
            }
            var pilots = team1 + team2 > 0 ? math.max(team1, team2) : players;

            // A group = the turrets of one team (the neutral pad ring is team 0). Its down clocks run only once all of
            // it is down (TurretRules.TickRespawn).
            var standing = new FixedList32Bytes<bool>();
            for (var i = 0; i <= Match.TeamRules.MaxTeams; i++) standing.Add(false);
            foreach (var (config, health) in SystemAPI.Query<RefRO<TurretConfig>, RefRO<Health>>())
                if (!health.ValueRO.IsDead && config.ValueRO.Team < standing.Length) standing[config.ValueRO.Team] = true;

            foreach (var (config, turret, health, pose, entity) in
                     SystemAPI.Query<RefRO<TurretConfig>, RefRW<TurretState>, RefRW<Health>, RefRO<LocalToWorld>>().WithEntityAccess())
            {
                ref var s = ref turret.ValueRW;
                var gun = config.ValueRO.Gun;
                if (s.Random.state == 0) s.Random = Random.CreateFromIndex((uint)entity.Index + 1u);
                if (math.lengthsq(s.Aim) < 1e-6f) s.Aim = pose.ValueRO.Forward;

                var max = TurretRules.HealthFor(config.ValueRO, pilots);
                if (max > 0f && math.abs(health.ValueRO.Max - max) > 0.5f)
                {
                    var share = health.ValueRO.Max > 0f ? health.ValueRO.Current / health.ValueRO.Max : 1f;
                    health.ValueRW.Max = max;
                    if (!health.ValueRO.IsDead) health.ValueRW.Current = math.max(share * max, 1f);
                }

                var groupDown = config.ValueRO.Team >= standing.Length || !standing[config.ValueRO.Team];
                if (TurretRules.TickRespawn(ref s, health.ValueRO.IsDead, config.ValueRO.RespawnSeconds, deltaTime, groupDown))
                    health.ValueRW.Current = health.ValueRO.Max;
                if (s.Down) continue;

                s.Cooldown -= deltaTime;
                var pivot = math.transform(pose.ValueRO.Value, gun.MuzzleOffset);

                // Nearest enemy in range with a clear line from the muzzle.
                var best = -1;
                var bestDistance = gun.Range;
                for (var i = 0; i < candidates.Length; i++)
                {
                    var c = candidates[i];
                    if (!TurretRules.IsEnemy(config.ValueRO.Team, c.Team)) continue;
                    var distance = math.distance(pivot, c.Position);
                    if (distance >= bestDistance) continue;
                    if (Explosions.TryRaycast(world, entity, pivot, c.Position, out var block) && block.Entity != c.Entity) continue;
                    best = i;
                    bestDistance = distance;
                }
                if (best < 0)
                {
                    s.Target = Entity.Null;
                    continue;
                }

                var target = candidates[best];
                s.Target = target.Entity;
                var aimPoint = target.Position;
                if (!Ballistics.IsInstant(gun.BulletSpeed))
                    Ballistics.LeadPoint(pivot, float3.zero, target.Position, target.Velocity, gun.BulletSpeed,
                        Ballistics.FlightTime(gun.Range, gun.BulletSpeed), out aimPoint, out _);
                var wanted = math.normalizesafe(aimPoint - pivot, s.Aim);
                s.Aim = TurretRules.Slew(s.Aim, wanted, config.ValueRO.TurnRate * deltaTime);

                if (!TurretRules.CanFire(s.Cooldown, s.Aim, wanted, config.ValueRO.FireCone)) continue;
                s.Cooldown = gun.FireInterval;
                s.ShotCount++;
                var direction = TurretRules.Scatter(s.Aim, config.ValueRO.Spread, ref s.Random);
                Ballistics.AddCapped(bullets, new ActiveBullet
                {
                    Position = TurretRules.Muzzle(pose.ValueRO.Value, config.ValueRO, s.Aim),
                    Velocity = Ballistics.LaunchVelocity(float3.zero, direction, gun.BulletSpeed),
                    TimeLeft = Ballistics.FlightTime(gun.Range, gun.BulletSpeed),
                    Shooter = entity,
                    Gun = gun,
                }, Ballistics.MaxRoundsPerShooter);
            }

            candidates.Dispose();
        }
    }
}
