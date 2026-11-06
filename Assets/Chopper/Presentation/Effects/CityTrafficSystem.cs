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
    /// Client only. The city's cosmetic life (realism list item 5): cars and trucks on the streets, the freeway and
    /// the sky road, flying cars over the main roads, drones round the towers, sweeping searchlights on the roofs.
    /// Data from the city scene (<see cref="CityTraffic"/>, from CityForge's "traffic").
    /// <list type="bullet">
    /// <item>Every vehicle follows its path at its own cruise speed and keeps a gap to the one in front (no overtaking:
    /// the vehicles of a path stay in order round it). Ground lanes stop at red lights before junctions (two phases
    /// per junction, <see cref="TrafficLook.SignalCycle"/>), so queues form and pull away.</item>
    /// <item>Loops go round; open lanes start again at their start, growing in and shrinking out at the ends.</item>
    /// <item>Flying cars bank into turns and pull pink tail-light trails; drones bob.</item>
    /// <item>Searchlight beams sweep the sky (slow turn, nodding up and down).</item>
    /// </list>
    /// Drawn with GPU instancing (one draw per model per 511 instances), no GameObjects, no colliders, no network:
    /// every player sees their own traffic. Written without the editor (6 Nov 2026).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CityTrafficSystem : PresentationSystemBase
    {
        private const int Batch = 511;
        private const int ModelCount = 4;     // car, truck, flyer, drone
        private const int FlyerModel = 2;
        private const int DroneModel = 3;
        /// <summary>The flyer's tail lights (Traffic_Flyer: tail bar at y 0.82, z -2.66, x ±0.9).</summary>
        private static readonly Vector3 FlyerTail = new(0f, 0.82f, -2.66f);
        private static readonly int ColourId = Shader.PropertyToID("_Colour");

        private BlobAssetReference<TrafficBlob> _blob;
        private int _count;
        private int[] _path;
        private int[] _segment;
        private int[] _leader;
        private float[] _distance;
        private float[] _cruise;
        private float[] _speed;
        private float[] _phase;
        private byte[] _model;
        private readonly Matrix4x4[][] _matrices = new Matrix4x4[ModelCount][];
        private readonly int[] _drawn = new int[ModelCount];
        private readonly Mesh[] _meshes = new Mesh[ModelCount];
        private Matrix4x4[] _trails = new Matrix4x4[1];
        private int _trailCount;
        private Matrix4x4[] _beams = new Matrix4x4[1];
        private Mesh _trailMesh;
        private Mesh _beamMesh;
        private MaterialPropertyBlock _beamColour;
        private MaterialPropertyBlock _floodColour;
        private Matrix4x4[] _jibs = new Matrix4x4[1];
        private Matrix4x4[] _floods = new Matrix4x4[1];
        private Mesh _hazeMesh;

        protected override void OnDestroy()
        {
            if (_trailMesh != null) Object.Destroy(_trailMesh);
            if (_hazeMesh != null) Object.Destroy(_hazeMesh);
            if (_beamMesh != null) Object.Destroy(_beamMesh);
        }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton<CityTraffic>(out var traffic) || !traffic.Paths.IsCreated)
            {
                _blob = default;
                _count = 0;
                return;
            }
            if (!_blob.Equals(traffic.Paths)) Spawn(traffic);

            var eye = CameraPosition();
            var time = (float)SystemAPI.Time.ElapsedTime;
            var material = traffic.Material.Value;
            _meshes[0] = traffic.Car.Value;
            _meshes[1] = traffic.Truck.Value;
            _meshes[2] = traffic.Flyer.Value;
            _meshes[3] = traffic.Drone.Value;
            if (_count > 0 && material != null && _meshes[0] != null)
            {
                Move(ref traffic.Paths.Value, traffic.Look, SystemAPI.Time.DeltaTime, time, eye, traffic.Trail.Value != null);
                DrawVehicles(material);
                DrawTrails(traffic.Trail.Value);
            }
            DrawBeams(ref traffic.Paths.Value, traffic.Look, traffic.Beam.Value, time);
            DrawCranes(ref traffic.Paths.Value, traffic.Look, material, traffic.Jib.Value, traffic.Beam.Value, time);
            DrawHaze(traffic.Haze.Value);
        }

        private Vector3 CameraPosition()
        {
            if (TryGetContext(out var context) && context.CameraRig != null) return context.CameraRig.transform.position;
            var main = UnityEngine.Camera.main;
            return main != null ? main.transform.position : Vector3.zero;
        }

        // -- spawn -------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Places the vehicles: per path, its length x the kind's density (scaled down to the cap), spread evenly with a
        /// little jitter, in order along the path (each one's leader is the next).
        /// </summary>
        private void Spawn(in CityTraffic traffic)
        {
            _blob = traffic.Paths;
            if (_hazeMesh != null) Object.Destroy(_hazeMesh);
            _hazeMesh = HazeMesh(ref traffic.Paths.Value.Lava);
            ref var paths = ref traffic.Paths.Value.Paths;
            var look = traffic.Look;
            var wanted = 0f;
            for (var i = 0; i < paths.Length; i++) wanted += Wanted(ref paths[i], look);
            var scale = look.MaxVehicles > 0 && wanted > look.MaxVehicles ? look.MaxVehicles / wanted : 1f;

            var random = new Random(0x5eed1234u);
            var counts = new int[paths.Length];
            var total = 0;
            for (var i = 0; i < paths.Length; i++)
            {
                var exact = Wanted(ref paths[i], look) * scale;
                // Never more than fit nose to nose.
                var room = (int)(paths[i].Length / math.max(1f, look.MinGap * 1.5f));
                counts[i] = math.min(room, (int)exact + (random.NextFloat() < exact - (int)exact ? 1 : 0));
                total += counts[i];
            }

            _path = new int[total];
            _segment = new int[total];
            _leader = new int[total];
            _distance = new float[total];
            _cruise = new float[total];
            _speed = new float[total];
            _phase = new float[total];
            _model = new byte[total];
            for (var m = 0; m < ModelCount; m++) _matrices[m] = new Matrix4x4[math.max(1, total)];
            _trails = new Matrix4x4[math.max(1, total)];

            var v = 0;
            for (var i = 0; i < paths.Length; i++)
            {
                ref var path = ref paths[i];
                var first = v;
                var spacing = path.Length / math.max(1, counts[i]);
                for (var k = 0; k < counts[i]; k++, v++)
                {
                    _path[v] = i;
                    _distance[v] = (k + random.NextFloat(0f, 0.5f)) * spacing;
                    _segment[v] = 0;
                    _cruise[v] = path.Speed * (1f + random.NextFloat(-look.SpeedJitter, look.SpeedJitter));
                    _speed[v] = _cruise[v];
                    _phase[v] = random.NextFloat(0f, math.PI * 2f);
                    _model[v] = path.Kind switch
                    {
                        TrafficKind.Air => FlyerModel,
                        TrafficKind.Drone => DroneModel,
                        _ => (byte)(random.NextFloat() < look.TruckShare ? 1 : 0),
                    };
                    // The next one along leads; the last one's leader is the first (round a loop, or after it wraps).
                    _leader[v] = k + 1 < counts[i] ? v + 1 : first;
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

        // -- move --------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Speeds (follow the leader, stop at red lights), then positions; writes the matrices of the vehicles near
        /// enough to draw, and the flying cars' trails.
        /// </summary>
        private void Move(ref TrafficBlob blob, in TrafficLook look, float deltaTime, float time, Vector3 eye, bool trails)
        {
            for (var m = 0; m < ModelCount; m++) _drawn[m] = 0;
            _trailCount = 0;
            var farSq = look.DrawDistance * look.DrawDistance;
            var cycle = math.max(1f, look.SignalCycle);
            var inCycle = time - cycle * math.floor(time / cycle);
            var green0 = inCycle < look.SignalGreen;
            var green1 = inCycle >= cycle * 0.5f && inCycle < cycle * 0.5f + look.SignalGreen;
            ref var paths = ref blob.Paths;

            // Speeds first, from where everyone was at the end of the last frame.
            for (var v = 0; v < _count; v++)
            {
                ref var path = ref paths[_path[v]];
                var limit = _cruise[v];
                var leader = _leader[v];
                if (leader != v)
                {
                    var gap = _distance[leader] - _distance[v];
                    if (gap < 0f) gap = path.Closed ? gap + path.Length : float.MaxValue;   // open lane: the leader wrapped
                    limit = math.min(limit, SafeSpeed(gap - look.MinGap, look.Braking));
                }
                var stop = NextRedStop(ref path, _distance[v], green0, green1);
                if (stop >= 0f)
                {
                    var toStop = stop - _distance[v];
                    // Too close to stop in time (the light just changed): carry on through.
                    var canStop = toStop > _speed[v] * _speed[v] / (2f * look.Braking) * 0.6f;
                    if (canStop) limit = math.min(limit, SafeSpeed(toStop - 1f, look.Braking));
                }
                _speed[v] = limit < _speed[v]
                    ? math.max(limit, _speed[v] - look.Braking * 1.5f * deltaTime)
                    : math.min(limit, _speed[v] + look.Acceleration * deltaTime);
            }

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
                if (model == DroneModel)
                {
                    position.y += math.sin(time * 1.7f + _phase[v]) * look.DroneBob;
                }
                else if (model == FlyerModel)
                {
                    // Bank into the turn (Unity: a right turn has a positive cross y; rolling right is a negative angle).
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
                var matrix = Matrix4x4.TRS(position, rotation, new Vector3(size, size, size));
                _matrices[model][_drawn[model]++] = matrix;

                if (trails && model == FlyerModel && look.TrailSeconds > 0f)
                {
                    // The trail mesh runs from z 0 back to z -1 at the tail lights: stretch it by speed x seconds.
                    var length = math.max(0.5f, _speed[v] * look.TrailSeconds) * size;
                    _trails[_trailCount++] = matrix * Matrix4x4.TRS(FlyerTail, Quaternion.identity, new Vector3(1f, 1f, length / size));
                }
            }
        }

        /// <summary>Highest speed that can still stop within <paramref name="room"/> metres.</summary>
        private static float SafeSpeed(float room, float braking) => math.sqrt(2f * braking * math.max(0f, room));

        /// <summary>Distance of the next stop line ahead whose phase is red, or -1 (none, or all green).</summary>
        private static float NextRedStop(ref TrafficPath path, float distance, bool green0, bool green1)
        {
            ref var stops = ref path.Stops;
            for (var i = 0; i < stops.Length; i++)
            {
                var stop = stops[i];
                if (stop.x < distance) continue;
                var green = stop.y < 0.5f ? green0 : green1;
                return green ? -1f : stop.x;
            }
            return -1f;
        }

        // -- draw --------------------------------------------------------------------------------------------------------

        private static RenderParams Params(Material material, bool shadows) => new(material)
        {
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = shadows,
            lightProbeUsage = LightProbeUsage.Off,
            worldBounds = new Bounds(Vector3.zero, new Vector3(20000f, 4000f, 20000f)),
        };

        private void DrawVehicles(Material material)
        {
            var parameters = Params(material, shadows: true);
            for (var m = 0; m < ModelCount; m++)
            {
                var mesh = _meshes[m];
                if (mesh == null) continue;
                for (var start = 0; start < _drawn[m]; start += Batch)
                    Graphics.RenderMeshInstanced(parameters, mesh, 0, _matrices[m], math.min(Batch, _drawn[m] - start), start);
            }
        }

        private void DrawTrails(Material material)
        {
            if (material == null || _trailCount == 0) return;
            _trailMesh ??= TrailMesh();
            var parameters = Params(material, shadows: false);
            for (var start = 0; start < _trailCount; start += Batch)
                Graphics.RenderMeshInstanced(parameters, _trailMesh, 0, _trails, math.min(Batch, _trailCount - start), start);
        }

        /// <summary>Searchlights: a slow turn plus a nod between 50 and 80 degrees up, each at its own phase and speed;
        /// drawn in colour groups (one draw per light: there are only a few).</summary>
        private void DrawBeams(ref TrafficBlob blob, in TrafficLook look, Material material, float time)
        {
            ref var lights = ref blob.Searchlights;
            if (material == null || lights.Length == 0) return;
            _beamMesh ??= BeamMesh();
            _beamColour ??= new MaterialPropertyBlock();
            if (_beams.Length < 1) _beams = new Matrix4x4[1];
            var baseColour = material.GetColor(ColourId);
            var parameters = Params(material, shadows: false);
            for (var i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                var yaw = light.Phase + time * light.Speed + 0.6f * math.sin(time * light.Speed * 1.7f + light.Phase * 3f);
                var pitch = math.radians(65f + 15f * math.sin(time * light.Speed * 1.3f + light.Phase));
                var direction = new float3(math.cos(pitch) * math.sin(yaw), math.sin(pitch), math.cos(pitch) * math.cos(yaw));
                var rotation = quaternion.LookRotationSafe(direction, new float3(0f, 1f, 0f));
                _beams[0] = Matrix4x4.TRS(light.Position, rotation, new Vector3(look.BeamRadius, look.BeamRadius, look.BeamLength));
                _beamColour.SetColor(ColourId, baseColour * new Color(light.Colour.x, light.Colour.y, light.Colour.z, 1f));
                parameters.matProps = _beamColour;
                Graphics.RenderMeshInstanced(parameters, _beamMesh, 0, _beams, 1);
            }
        }

        /// <summary>
        /// Tower cranes: each jib swings slowly round its mast top (a sine about its centre angle); the floodlights under
        /// it throw warm cones down to the ground (y 0, the city floor).
        /// </summary>
        private void DrawCranes(ref TrafficBlob blob, in TrafficLook look, Material material, Mesh jib, Material beam, float time)
        {
            ref var cranes = ref blob.Cranes;
            if (cranes.Length == 0 || material == null || jib == null) return;
            ref var floods = ref blob.Floods;
            if (_jibs.Length < cranes.Length) _jibs = new Matrix4x4[cranes.Length];
            if (_floods.Length < cranes.Length * math.max(1, floods.Length)) _floods = new Matrix4x4[cranes.Length * math.max(1, floods.Length)];
            var floodCount = 0;
            var down = quaternion.LookRotationSafe(new float3(0f, -1f, 0f), new float3(0f, 0f, 1f));
            for (var i = 0; i < cranes.Length; i++)
            {
                var crane = cranes[i];
                var yaw = crane.Phase + crane.Swing * math.sin(time * crane.Speed);
                var rotation = quaternion.RotateY(yaw);
                _jibs[i] = Matrix4x4.TRS(crane.Pivot, rotation, Vector3.one);
                for (var k = 0; k < floods.Length; k++)
                {
                    var spot = crane.Pivot + math.mul(rotation, new float3(0f, 0.75f, floods[k]));
                    var height = math.max(1f, spot.y);
                    _floods[floodCount++] = Matrix4x4.TRS(spot, down, new Vector3(look.FloodRadius, look.FloodRadius, height));
                }
            }
            Graphics.RenderMeshInstanced(Params(material, shadows: true), jib, 0, _jibs, cranes.Length);
            if (beam == null || floodCount == 0) return;
            _beamMesh ??= BeamMesh();
            _floodColour ??= new MaterialPropertyBlock();
            _floodColour.SetColor(ColourId, beam.GetColor(ColourId) * new Color(look.FloodColour.x, look.FloodColour.y, look.FloodColour.z, 1f) * 2f);
            var parameters = Params(beam, shadows: false);
            parameters.matProps = _floodColour;
            Graphics.RenderMeshInstanced(parameters, _beamMesh, 0, _floods, floodCount);
        }

        private void DrawHaze(Material material)
        {
            if (material == null || _hazeMesh == null) return;
            var parameters = Params(material, shadows: false);
            parameters.worldBounds = _hazeMesh.bounds;
            Graphics.RenderMesh(parameters, _hazeMesh, 0, Matrix4x4.identity);
        }

        /// <summary>
        /// Heat shimmer quads along the lava (HeatHaze shader): one upright billboard every point (8 m), as wide as the
        /// lava plus 6 m and 10 m + a tenth of the width high. POSITION = the foot on the lava, UV1 = the corner offset
        /// (m) that the shader turns to face the camera. Null when the map has no lava.
        /// </summary>
        private static Mesh HazeMesh(ref BlobArray<LavaPath> lava)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var corners = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();
            var bounds = new Bounds();
            var first = true;
            for (var i = 0; i < lava.Length; i++)
            {
                ref var path = ref lava[i];
                var half = path.Width * 0.5f + 3f;
                var height = 10f + path.Width * 0.1f;
                for (var k = 0; k < path.Points.Length; k++)
                {
                    Vector3 foot = path.Points[k];
                    var start = vertices.Count;
                    for (var c = 0; c < 4; c++)
                    {
                        var u = c % 2;
                        var v = c / 2;
                        vertices.Add(foot);
                        uvs.Add(new Vector2(u, v));
                        corners.Add(new Vector2((u * 2 - 1) * half, v * height));
                    }
                    triangles.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
                    var box = new Bounds(foot + Vector3.up * height * 0.5f, new Vector3(half * 2f, height, half * 2f));
                    if (first) bounds = box;
                    else bounds.Encapsulate(box);
                    first = false;
                }
            }
            if (vertices.Count == 0) return null;
            var mesh = new Mesh { name = "Heat Haze", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, corners);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = bounds;
            return mesh;
        }

        /// <summary>Two crossed strips (flat and upright) behind each tail light, z 0 to -1; uv.x across, uv.y along.</summary>
        private static Mesh TrailMesh()
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            const float half = 0.18f;
            foreach (var x in new[] { -0.9f, 0.9f })
            {
                foreach (var flat in new[] { true, false })
                {
                    var across = flat ? new Vector3(half, 0f, 0f) : new Vector3(0f, half, 0f);
                    var normal = flat ? Vector3.up : Vector3.right;
                    var centre = new Vector3(x, 0f, 0f);
                    var first = vertices.Count;
                    vertices.Add(centre - across);
                    vertices.Add(centre + across);
                    vertices.Add(centre - across + Vector3.back);
                    vertices.Add(centre + across + Vector3.back);
                    uvs.Add(new Vector2(0f, 0f));
                    uvs.Add(new Vector2(1f, 0f));
                    uvs.Add(new Vector2(0f, 1f));
                    uvs.Add(new Vector2(1f, 1f));
                    for (var k = 0; k < 4; k++) normals.Add(normal);
                    triangles.AddRange(new[] { first, first + 2, first + 1, first + 1, first + 2, first + 3 });
                }
            }
            var mesh = new Mesh { name = "Traffic Trail" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(new Vector3(0f, 0f, -0.5f), new Vector3(2.5f, 0.5f, 1.2f));
            return mesh;
        }

        /// <summary>An open cone from the apex (origin) along +Z to radius 1 at z 1; normals outward; uv.y along.</summary>
        private static Mesh BeamMesh()
        {
            const int sides = 24;
            var vertices = new Vector3[(sides + 1) * 2];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[sides * 6];
            for (var i = 0; i <= sides; i++)
            {
                var angle = i / (float)sides * Mathf.PI * 2f;
                var ring = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                var normal = (ring - new Vector3(0f, 0f, 1f) * 1f).normalized;   // the cone is 45 degrees in mesh space
                vertices[i * 2] = ring * 0.02f;                                    // a small lens, not a point
                vertices[i * 2 + 1] = ring + Vector3.forward;
                normals[i * 2] = normals[i * 2 + 1] = normal;
                uvs[i * 2] = new Vector2(i / (float)sides, 0f);
                uvs[i * 2 + 1] = new Vector2(i / (float)sides, 1f);
            }
            for (var i = 0; i < sides; i++)
            {
                var a = i * 2;
                triangles[i * 6] = a;
                triangles[i * 6 + 1] = a + 1;
                triangles[i * 6 + 2] = a + 2;
                triangles[i * 6 + 3] = a + 2;
                triangles[i * 6 + 4] = a + 1;
                triangles[i * 6 + 5] = a + 3;
            }
            var mesh = new Mesh { name = "Searchlight Beam", vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            mesh.bounds = new Bounds(new Vector3(0f, 0f, 0.5f), new Vector3(2.2f, 2.2f, 1.2f));
            return mesh;
        }
    }
}
