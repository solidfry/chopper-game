using Chopper.Presentation.Bridge;
using Chopper.Simulation.Visuals;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. The city's cosmetic traffic (realism list item 5, "life"): cars and trucks on the streets, the
    /// freeway and the sky road, flying cars over the main roads, drones round the towers. Paths and numbers come from
    /// the city scene (<see cref="CityTraffic"/>, from CityForge's "traffic"). Every vehicle moves along its path at
    /// its own speed; loops go round, open lanes start again at their start (growing in, shrinking out at the ends).
    /// Drawn with GPU instancing (one draw per model per 511 vehicles), no GameObjects, no colliders, no network:
    /// every player sees their own traffic. Head and tail lights are palette glow swatches, so bloom shows them.
    /// Written without the editor (6 Nov 2026).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CityTrafficSystem : PresentationSystemBase
    {
        private const int Batch = 511;
        private const int ModelCount = 4;     // car, truck, flyer, drone

        private BlobAssetReference<TrafficBlob> _blob;
        private int _count;
        private int[] _path;
        private int[] _segment;
        private float[] _distance;
        private float[] _speed;
        private float[] _phase;
        private byte[] _model;
        private readonly Matrix4x4[][] _matrices = new Matrix4x4[ModelCount][];
        private readonly int[] _drawn = new int[ModelCount];
        private readonly Mesh[] _meshes = new Mesh[ModelCount];

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<CityTraffic>(out var traffic) || !traffic.Paths.IsCreated)
            {
                _blob = default;
                _count = 0;
                return;
            }
            if (!_blob.Equals(traffic.Paths)) Spawn(traffic);
            if (_count == 0) return;

            var material = traffic.Material.Value;
            _meshes[0] = traffic.Car.Value;
            _meshes[1] = traffic.Truck.Value;
            _meshes[2] = traffic.Flyer.Value;
            _meshes[3] = traffic.Drone.Value;
            if (material == null || _meshes[0] == null) return;

            var eye = CameraPosition();
            Move(ref traffic.Paths.Value, traffic.Look, SystemAPI.Time.DeltaTime, (float)SystemAPI.Time.ElapsedTime, eye);
            Draw(material);
        }

        private Vector3 CameraPosition()
        {
            if (TryGetContext(out var context) && context.CameraRig != null) return context.CameraRig.transform.position;
            var main = Camera.main;
            return main != null ? main.transform.position : Vector3.zero;
        }

        /// <summary>Places the vehicles: per path, its length x the kind's density (scaled down to the cap).</summary>
        private void Spawn(in CityTraffic traffic)
        {
            _blob = traffic.Paths;
            ref var paths = ref traffic.Paths.Value.Paths;
            var look = traffic.Look;
            var wanted = 0f;
            for (var i = 0; i < paths.Length; i++) wanted += Wanted(ref paths[i], look);
            var scale = look.MaxVehicles > 0 && wanted > look.MaxVehicles ? look.MaxVehicles / wanted : 1f;

            var random = new Random(0x5eed1234u);
            var total = 0;
            var counts = new int[paths.Length];
            for (var i = 0; i < paths.Length; i++)
            {
                var exact = Wanted(ref paths[i], look) * scale;
                counts[i] = (int)exact + (random.NextFloat() < exact - (int)exact ? 1 : 0);
                total += counts[i];
            }
            total = math.min(total, look.MaxVehicles > 0 ? look.MaxVehicles : total);

            _count = total;
            _path = new int[total];
            _segment = new int[total];
            _distance = new float[total];
            _speed = new float[total];
            _phase = new float[total];
            _model = new byte[total];
            for (var m = 0; m < ModelCount; m++) _matrices[m] = new Matrix4x4[math.max(1, total)];

            var v = 0;
            for (var i = 0; i < paths.Length && v < total; i++)
            {
                ref var path = ref paths[i];
                for (var k = 0; k < counts[i] && v < total; k++, v++)
                {
                    _path[v] = i;
                    _distance[v] = random.NextFloat(0f, path.Length);
                    _segment[v] = 0;
                    _speed[v] = path.Speed * (1f + random.NextFloat(-look.SpeedJitter, look.SpeedJitter));
                    _phase[v] = random.NextFloat(0f, math.PI * 2f);
                    _model[v] = path.Kind switch
                    {
                        TrafficKind.Air => 2,
                        TrafficKind.Drone => 3,
                        _ => (byte)(random.NextFloat() < look.TruckShare ? 1 : 0),
                    };
                }
            }
            _count = v;
        }

        private static float Wanted(ref TrafficPath path, in TrafficLook look)
        {
            var kind = (int)path.Kind;
            var perKm = kind < look.PerKm.Length ? look.PerKm[kind] : 0f;
            return path.Length / 1000f * perKm;
        }

        /// <summary>Moves every vehicle along its path and writes the matrices of those near enough to draw.</summary>
        private void Move(ref TrafficBlob blob, in TrafficLook look, float deltaTime, float time, Vector3 eye)
        {
            for (var m = 0; m < ModelCount; m++) _drawn[m] = 0;
            var farSq = look.DrawDistance * look.DrawDistance;
            ref var paths = ref blob.Paths;
            for (var v = 0; v < _count; v++)
            {
                ref var path = ref paths[_path[v]];
                var distance = _distance[v] + _speed[v] * deltaTime;
                var segment = _segment[v];
                if (distance >= path.Length)
                {
                    distance -= path.Length * math.floor(distance / path.Length);
                    segment = 0;
                }
                var segments = path.SegmentCount;
                while (segment < segments - 1 && path.Distances[segment + 1] <= distance) segment++;
                _distance[v] = distance;
                _segment[v] = segment;

                var count = path.Points.Length;
                var a = path.Points[segment];
                var b = path.Points[(segment + 1) % count];
                var start = path.Distances[segment];
                var span = math.max(1e-3f, path.Distances[segment + 1] - start);
                var t = math.saturate((distance - start) / span);
                var position = math.lerp(a, b, t);
                if (math.distancesq(position, (float3)eye) > farSq) continue;

                // Heading: this segment's direction, turning into the next one over the second half of the segment.
                var direction = math.normalizesafe(b - a, new float3(0f, 0f, 1f));
                var hasNext = path.Closed || segment + 2 < count;
                var next = hasNext ? math.normalizesafe(path.Points[(segment + 2) % count] - b, direction) : direction;
                var blend = math.smoothstep(0.5f, 1f, t);
                var forward = math.normalizesafe(math.lerp(direction, next, blend), direction);

                var model = _model[v];
                var up = new float3(0f, 1f, 0f);
                if (model == 3)
                {
                    position.y += math.sin(time * 1.7f + _phase[v]) * look.DroneBob;
                }
                else if (model == 2)
                {
                    // Bank into the turn: the turn between this segment and the next, signed (left = negative y).
                    var turn = math.cross(direction, next).y;
                    var roll = math.radians(math.clamp(-turn * 120f, -look.FlyerBank, look.FlyerBank)) * blend;
                    up = math.mul(quaternion.AxisAngle(forward, roll), up);
                    position.y += math.sin(time * 0.9f + _phase[v]) * 0.25f;
                }

                var size = 1f;
                if (!path.Closed && look.EndFade > 0f)
                    size = math.saturate(distance / look.EndFade) * math.saturate((path.Length - distance) / look.EndFade);
                if (size <= 0.01f) continue;

                var rotation = quaternion.LookRotationSafe(forward, up);
                _matrices[model][_drawn[model]++] = Matrix4x4.TRS(position, rotation, new Vector3(size, size, size));
            }
        }

        private void Draw(Material material)
        {
            var parameters = new RenderParams(material)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                lightProbeUsage = LightProbeUsage.Off,
                worldBounds = new Bounds(Vector3.zero, new Vector3(20000f, 4000f, 20000f)),
            };
            for (var m = 0; m < ModelCount; m++)
            {
                var mesh = _meshes[m];
                if (mesh == null) continue;
                for (var start = 0; start < _drawn[m]; start += Batch)
                    Graphics.RenderMeshInstanced(parameters, mesh, 0, _matrices[m], math.min(Batch, _drawn[m] - start), start);
            }
        }
    }
}
