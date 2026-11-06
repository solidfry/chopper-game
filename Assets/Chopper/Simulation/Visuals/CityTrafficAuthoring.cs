using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Visuals
{
    /// <summary>Traffic path kinds, as CityForge writes them (<c>Tools/CityForge/cityforge/traffic.py</c>).</summary>
    public enum TrafficKind : byte
    {
        Street,
        Main,
        Highway,
        Freeway,
        Sky,
        Spiral,
        Air,
        Drone,
    }

    /// <summary>One traffic path: points along a lane or air route, and the distance along it at each point.</summary>
    public struct TrafficPath
    {
        public TrafficKind Kind;
        /// <summary>A loop: the last point joins the first.</summary>
        public bool Closed;
        /// <summary>Cruise speed, m/s.</summary>
        public float Speed;
        /// <summary>Total length, m (with the closing segment of a loop).</summary>
        public float Length;
        public BlobArray<float3> Points;
        /// <summary>Distance along the path at each point; a loop has one more (its full length, back at the start).</summary>
        public BlobArray<float> Distances;

        public int SegmentCount => Closed ? Points.Length : Points.Length - 1;
    }

    public struct TrafficBlob
    {
        public BlobArray<TrafficPath> Paths;
    }

    /// <summary>
    /// The map's cosmetic traffic: paths (blob), the vehicle meshes and the instanced material, and how much traffic
    /// to show. Baked from the city scene (Setup step 8 adds it from <c>&lt;kit&gt;.terrain.json</c> "traffic"); the
    /// client's <c>CityTrafficSystem</c> moves and draws the vehicles. Never on the server, no colliders, not networked.
    /// </summary>
    public struct CityTraffic : IComponentData
    {
        public BlobAssetReference<TrafficBlob> Paths;
        public UnityObjectRef<Mesh> Car;
        public UnityObjectRef<Mesh> Truck;
        public UnityObjectRef<Mesh> Flyer;
        public UnityObjectRef<Mesh> Drone;
        public UnityObjectRef<Material> Material;
        public TrafficLook Look;
    }

    /// <summary>Traffic numbers, copied from <see cref="TrafficSettings"/> when the city scene bakes.</summary>
    public struct TrafficLook
    {
        /// <summary>Vehicles per km of path, by <see cref="TrafficKind"/> (index = kind).</summary>
        public FixedList64Bytes<float> PerKm;
        public float TruckShare;
        public float SpeedJitter;
        public float DrawDistance;
        public int MaxVehicles;
        public float DroneBob;
        public float FlyerBank;
        /// <summary>Open paths: vehicles grow from nothing over this distance at the start and shrink at the end (m).</summary>
        public float EndFade;
    }

    [DisallowMultipleComponent]
    public sealed class CityTrafficAuthoring : MonoBehaviour
    {
        [Serializable]
        public sealed class PathData
        {
            public TrafficKind kind;
            public bool closed;
            public float speed;
            public Vector3[] points;
        }

        [SerializeField, Tooltip("Shared traffic numbers (Data/Rendering/TrafficSettings.asset). Empty: the defaults.")]
        private TrafficSettings settings;
        [SerializeField] private Mesh car;
        [SerializeField] private Mesh truck;
        [SerializeField] private Mesh flyer;
        [SerializeField] private Mesh drone;
        [SerializeField, Tooltip("The city palette material with GPU instancing on.")] private Material material;
        [SerializeField, HideInInspector] private List<PathData> paths = new();

        public int PathCount => paths.Count;

        /// <summary>Editor only: used by the setup tool (CityForge import).</summary>
        public void Set(TrafficSettings trafficSettings, Mesh carMesh, Mesh truckMesh, Mesh flyerMesh, Mesh droneMesh, Material instanced,
            List<PathData> pathData)
        {
            settings = trafficSettings;
            car = carMesh;
            truck = truckMesh;
            flyer = flyerMesh;
            drone = droneMesh;
            material = instanced;
            paths = pathData;
        }

        private sealed class Baker : Baker<CityTrafficAuthoring>
        {
            public override void Bake(CityTrafficAuthoring authoring)
            {
                if (authoring.material == null || authoring.paths == null || authoring.paths.Count == 0) return;
                if (authoring.settings != null) DependsOn(authoring.settings);
                var entity = GetEntity(TransformUsageFlags.None);
                var blob = Build(authoring.paths);
                AddBlobAsset(ref blob, out _);
                AddComponent(entity, new CityTraffic
                {
                    Paths = blob,
                    Car = authoring.car,
                    Truck = authoring.truck != null ? authoring.truck : authoring.car,
                    Flyer = authoring.flyer != null ? authoring.flyer : authoring.car,
                    Drone = authoring.drone != null ? authoring.drone : authoring.flyer,
                    Material = authoring.material,
                    Look = authoring.settings != null ? authoring.settings.ToLook() : TrafficSettings.DefaultLook(),
                });
            }

            private static BlobAssetReference<TrafficBlob> Build(List<PathData> source)
            {
                var builder = new BlobBuilder(Allocator.Temp);
                ref var root = ref builder.ConstructRoot<TrafficBlob>();
                var usable = source.FindAll(p => p.points != null && p.points.Length >= 2);
                var paths = builder.Allocate(ref root.Paths, usable.Count);
                for (var i = 0; i < usable.Count; i++)
                {
                    var data = usable[i];
                    ref var path = ref paths[i];
                    path.Kind = data.kind;
                    path.Closed = data.closed;
                    path.Speed = math.max(0.5f, data.speed);
                    var count = data.points.Length;
                    var points = builder.Allocate(ref path.Points, count);
                    var distances = builder.Allocate(ref path.Distances, data.closed ? count + 1 : count);
                    var run = 0f;
                    for (var k = 0; k < count; k++)
                    {
                        points[k] = data.points[k];
                        if (k > 0) run += Vector3.Distance(data.points[k - 1], data.points[k]);
                        distances[k] = run;
                    }
                    if (data.closed)
                    {
                        run += Vector3.Distance(data.points[count - 1], data.points[0]);
                        distances[count] = run;
                    }
                    path.Length = math.max(run, 0.01f);
                }
                var blob = builder.CreateBlobAssetReference<TrafficBlob>(Allocator.Persistent);
                builder.Dispose();
                return blob;
            }
        }
    }
}
