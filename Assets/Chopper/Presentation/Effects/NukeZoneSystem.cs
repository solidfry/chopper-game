using Chopper.Presentation.Bridge;
using Chopper.Simulation.Match;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Capture the Nuke pick-up zone (owner, 20 Nov: "an area indicator for pick up ... each player should see
    /// their own pick up zone size"): a glowing dome round the nuke while nobody holds it, its radius the local chopper's
    /// own hook range (<see cref="NukeHookConfig.Range"/>: 12 m, the Heavy Winch 18 m), the same 3D distance the server
    /// tests. Brighter while the local chopper is inside it. Drawn with <see cref="Graphics.RenderMesh"/>: no GameObject.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NukeZoneSystem : PresentationSystemBase
    {
        /// <summary>Colour scale while the local chopper is in range (it can hook).</summary>
        private const float InRangeBoost = 2.2f;

        private Mesh _dome;
        private MaterialPropertyBlock _block;
        private static readonly int ColourId = Shader.PropertyToID("_Colour");

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            if (_dome != null) Object.Destroy(_dome);
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context) || context.Match.Mode != MatchMode.CaptureTheNuke) return;
            var material = context.Effects.Settings != null ? context.Effects.Settings.NukeZoneMaterial : null;
            if (material == null) return;

            var range = NukeHookConfig.Base.Range;
            var self = float3.zero;
            var hasSelf = false;
            foreach (var (pose, chopper) in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<GhostOwnerIsLocal, NukeCarry>().WithEntityAccess())
            {
                if (SystemAPI.HasComponent<NukeHookConfig>(chopper)) range = SystemAPI.GetComponent<NukeHookConfig>(chopper).Range;
                self = pose.ValueRO.Position;
                hasSelf = true;
            }

            foreach (var (nuke, pose) in SystemAPI.Query<RefRO<NukeState>, RefRO<LocalToWorld>>())
            {
                if (nuke.ValueRO.Phase is not (NukePhase.AtSpawn or NukePhase.Dropped)) continue;
                var centre = pose.ValueRO.Position;
                var inside = hasSelf && math.distance(self, centre) <= range;
                _dome ??= Dome(32, 12);
                _block ??= new MaterialPropertyBlock();
                var colour = material.GetColor(ColourId);
                _block.SetColor(ColourId, inside ? colour * InRangeBoost : colour);
                var parameters = new RenderParams(material) { matProps = _block, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off };
                Graphics.RenderMesh(parameters, _dome, 0, Matrix4x4.TRS(centre, Quaternion.identity, Vector3.one * range));
            }
        }

        /// <summary>Unit hemisphere (y up), uv.y 0 at the rim to 1 at the top, normals out.</summary>
        private static Mesh Dome(int around, int up)
        {
            var vertices = new Vector3[(around + 1) * (up + 1)];
            var uvs = new Vector2[vertices.Length];
            for (var j = 0; j <= up; j++)
            for (var i = 0; i <= around; i++)
            {
                var a = i * Mathf.PI * 2f / around;
                var b = j * Mathf.PI * 0.5f / up;
                var k = j * (around + 1) + i;
                vertices[k] = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(b), Mathf.Sin(a) * Mathf.Cos(b));
                uvs[k] = new Vector2((float)i / around, (float)j / up);
            }
            var triangles = new int[around * up * 6];
            var t = 0;
            for (var j = 0; j < up; j++)
            for (var i = 0; i < around; i++)
            {
                var k = j * (around + 1) + i;
                triangles[t++] = k; triangles[t++] = k + around + 1; triangles[t++] = k + 1;
                triangles[t++] = k + 1; triangles[t++] = k + around + 1; triangles[t++] = k + around + 2;
            }
            var mesh = new Mesh { name = "Nuke Zone Dome", vertices = vertices, uv = uvs, triangles = triangles };
            mesh.normals = vertices;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
