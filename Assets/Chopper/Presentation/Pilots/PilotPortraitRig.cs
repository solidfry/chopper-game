using System;
using Chopper.Simulation.Loadout;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Pilots
{
    /// <summary>What the portrait reacts to this frame. Made by its owner (HUD system, select screen).</summary>
    public struct PilotPortraitInput
    {
        /// <summary>World rotation of the pilot: forward = where the pilot looks. The viewer is in front of the pilot.</summary>
        public Quaternion Facing;
        /// <summary>Chopper bank, positive = rolled left.</summary>
        public float RollDegrees;
        /// <summary>Degrees per second, positive = nose right.</summary>
        public float YawRate;
        public bool Boosting;
        /// <summary>Hull share lost this frame (0 = no hit). Shakes the head and flashes the picture.</summary>
        public float Hit;
        /// <summary>Voice loudness (RMS, 0..1) of this pilot's own line; 0 when someone else speaks.</summary>
        public float Voice;
        /// <summary>0..1, line glitches (jammed, a new pilot on the select screen).</summary>
        public float Glitch;
    }

    /// <summary>
    /// The pilot's portrait as a live picture (plan section 5b): a small render texture that the HUD and the pilot select
    /// screen show. Each <see cref="Tick"/> moves the "rig" (breath, blinks, idle head motion, head lean and look with the
    /// flight, a jaw that follows the voice, a shake on hits) and draws the art with <c>Chopper/PilotPortrait</c> in the
    /// scene's light: the brightest directional light and the ambient probe, seen from the pilot's facing.
    /// Owned by the <see cref="Bridge.PresentationContext"/>.
    /// </summary>
    public sealed class PilotPortraitRig : IDisposable
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int NormalTexId = Shader.PropertyToID("_NormalTex");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int FlipYId = Shader.PropertyToID("_FlipY");
        private static readonly int ViewId = Shader.PropertyToID("_View");
        private static readonly int HeadRigId = Shader.PropertyToID("_HeadRig");
        private static readonly int NeckMouthId = Shader.PropertyToID("_NeckMouth");
        private static readonly int EyesId = Shader.PropertyToID("_Eyes");
        private static readonly int FaceSizesId = Shader.PropertyToID("_FaceSizes");
        private static readonly int Pose0Id = Shader.PropertyToID("_Pose0");
        private static readonly int Pose1Id = Shader.PropertyToID("_Pose1");
        private static readonly int LightDirId = Shader.PropertyToID("_LightDir");
        private static readonly int LightColorId = Shader.PropertyToID("_LightColor");
        private static readonly int AmbientUpId = Shader.PropertyToID("_AmbientUp");
        private static readonly int AmbientMidId = Shader.PropertyToID("_AmbientMid");
        private static readonly int AmbientDownId = Shader.PropertyToID("_AmbientDown");
        private static readonly int FillDirId = Shader.PropertyToID("_FillDir");
        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int AccentId = Shader.PropertyToID("_Accent");
        private static readonly int StyleId = Shader.PropertyToID("_Style");
        private static readonly int FxId = Shader.PropertyToID("_Fx");
        private static readonly int LightShapeId = Shader.PropertyToID("_LightShape");

        /// <summary>Seconds a blink takes.</summary>
        private const float BlinkSeconds = 0.16f;
        /// <summary>Seconds between looks for the brightest directional light (lights can change with the scene).</summary>
        private const float SunSearchSeconds = 2f;

        private readonly PilotPortraitLook _look;
        private readonly Material _material;
        private readonly CommandBuffer _commands = new() { name = "Pilot Portrait" };
        private readonly Vector3[] _ambientDirections = new Vector3[3];
        private readonly Color[] _ambientColours = new Color[3];
        private PilotDefinition _pilot;
        private Light _sun;
        private float _sunSearch;
        private float _time;
        private float _seed;
        private float _breath;
        private float _blinkWait = 1f;
        private float _blinkTime = -1f;
        private float _voice;
        private float _shake;
        private float _flash;
        private float _glitch;
        private Vector2 _lean;

        public PilotPortraitRig(PilotPortraitLook look)
        {
            _look = look;
            if (look == null || look.Shader == null || !look.Shader.isSupported) return;
            _material = new Material(look.Shader) { hideFlags = HideFlags.HideAndDontSave };
            var size = new Vector2Int(Mathf.Max(16, look.Size.x), Mathf.Max(16, look.Size.y));
            Texture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Pilot Portrait",
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = false,
                filterMode = FilterMode.Bilinear,
            };
            Texture.Create();
        }

        /// <summary>Null when the look has no shader (Setup step 1 not run) or the platform cannot run it.</summary>
        public RenderTexture Texture { get; }

        /// <summary>A pilot with portrait art is set: the picture is worth showing.</summary>
        public bool HasPilot => Texture != null && _pilot != null && _pilot.Portrait != null;

        public PilotDefinition Pilot => _pilot;

        /// <summary>True (HUD): the portrait takes the UI green (<see cref="PilotPortraitLook.HudColour"/>). False (select
        /// screen): the pilot's own accent.</summary>
        public bool UiColour { get; set; }

        /// <summary>The pilot's accent colour (white with no pilot).</summary>
        public Color Accent => _pilot != null ? _pilot.Accent : Color.white;

        /// <summary>Loudness of this pilot's own radio line when it plays; 0 when another line (the announcer) plays.</summary>
        public float OwnVoice(Audio.IAudioService audio)
        {
            var line = _pilot != null ? _pilot.AbilityLine : null;
            return line != null && audio.VoiceClip == line ? audio.VoiceLevel : 0f;
        }

        /// <summary>Shows another pilot (null = none). The same pilot again changes nothing.</summary>
        public void SetPilot(PilotDefinition pilot)
        {
            if (pilot == _pilot) return;
            _pilot = pilot;
            _seed = pilot != null ? pilot.Id * 17.31f : 0f;
            _blinkWait = 0.6f;
            if (_material == null || pilot == null) return;
            _material.SetTexture(MainTexId, pilot.Portrait);
            var face = pilot.Face;
            _material.SetTexture(NormalTexId, face.NormalMap != null ? face.NormalMap : Texture2D.normalTexture);
            var art = pilot.Portrait;
            _material.SetFloat(AspectId, art != null && art.height > 0 ? (float)art.width / art.height : 2f / 3f);
            // Face points are from the top (as a paint program shows them); the shader wants UV, y up.
            _material.SetVector(HeadRigId, new Vector4(face.HeadCentre.x, 1f - face.HeadCentre.y, face.HeadSize.x, face.HeadSize.y));
            _material.SetVector(NeckMouthId, new Vector4(face.Neck.x, 1f - face.Neck.y, face.Mouth.x, 1f - face.Mouth.y));
            _material.SetVector(EyesId, new Vector4(
                face.HasLeftEye ? face.LeftEye.x : -1f, face.HasLeftEye ? 1f - face.LeftEye.y : -1f,
                face.HasRightEye ? face.RightEye.x : -1f, face.HasRightEye ? 1f - face.RightEye.y : -1f));
            _material.SetVector(FaceSizesId, new Vector4(face.EyeSize, Mathf.Max(0f, face.MouthWidth), 1f - face.Chin, face.Masked ? 1f : 0f));
            _material.SetVector(ViewId, View(face));
        }

        /// <summary>
        /// Head and shoulders: from a little above the head to a little below the neck, the head in the middle (UV, y up).
        /// The picture and the art are both 2:3, so the same part of each side keeps the shape.
        /// </summary>
        private static Vector4 View(PilotFace face)
        {
            var top = face.HeadCentre.y - face.HeadSize.y * 1.2f;
            var bottom = face.Neck.y + 0.16f;
            var size = Mathf.Clamp(bottom - top, 0.3f, 1f);
            var left = Mathf.Clamp(face.HeadCentre.x - size * 0.5f, 0f, 1f - size);
            top = Mathf.Clamp(top, 0f, 1f - size);
            return new Vector4(left, 1f - (top + size), size, size);
        }

        /// <summary>Moves the rig and draws the picture. Call once per frame while it shows.</summary>
        public void Tick(float deltaTime, in PilotPortraitInput input)
        {
            if (!HasPilot || _material == null) return;
            deltaTime = Mathf.Clamp(deltaTime, 0f, 0.1f);
            _time += deltaTime;
            var look = _look;

            // Breath: faster while boosting.
            var breathRate = (input.Boosting ? 2f : 1f) / Mathf.Max(0.5f, look.BreathSeconds);
            _breath += deltaTime * breathRate;
            var breath = 0.5f - 0.5f * Mathf.Cos(_breath * 2f * Mathf.PI);

            // Blinks at random waits; a hit makes the pilot blink.
            if (input.Hit > 0f && _blinkTime < 0f) _blinkTime = 0f;
            if (_blinkTime < 0f)
            {
                _blinkWait -= deltaTime;
                if (_blinkWait <= 0f)
                {
                    _blinkTime = 0f;
                    _blinkWait = UnityEngine.Random.Range(look.BlinkWait.x, Mathf.Max(look.BlinkWait.x, look.BlinkWait.y));
                }
            }
            var blink = 0f;
            if (_blinkTime >= 0f)
            {
                _blinkTime += deltaTime;
                blink = Mathf.Sin(Mathf.Clamp01(_blinkTime / BlinkSeconds) * Mathf.PI);
                if (_blinkTime >= BlinkSeconds) _blinkTime = -1f;
            }

            // Voice: quick to open, a little slower to close; syllables from a fast noise.
            var target = Mathf.Clamp01(input.Voice * look.TalkGain);
            _voice = Mathf.Lerp(_voice, target, 1f - Mathf.Exp(-(target > _voice ? 30f : 14f) * deltaTime));
            var jaw = _voice * (0.55f + 0.45f * Mathf.PerlinNoise(_time * 11f, _seed + 3f));

            // Hits: shake, flash and glitch, bigger for bigger hits; all fade out.
            if (input.Hit > 0f)
            {
                var strength = Mathf.Clamp01(input.Hit * 6f);
                _shake = Mathf.Max(_shake, 0.4f + strength);
                _flash = Mathf.Max(_flash, 0.25f + 0.5f * strength);
                _glitch = Mathf.Max(_glitch, 0.4f + 0.6f * strength);
            }
            _shake *= Mathf.Exp(-7f * deltaTime);
            _flash *= Mathf.Exp(-9f * deltaTime);
            _glitch *= Mathf.Exp(-4f * deltaTime);

            // Head: idle drift, a lean against the roll, a look into the turn, a buzz while boosting, hit shake.
            var idle = look.Idle;
            var drift = new Vector2(Noise(0.23f, 0f), Noise(0.19f, 5f)) * 0.006f * idle;
            var leanTarget = new Vector2(
                Mathf.Clamp(input.RollDegrees * look.RollLean, -0.05f, 0.05f) + Noise(0.17f, 9f) * 0.035f * idle,
                Mathf.Clamp(-input.YawRate / Mathf.Max(1f, look.TurnLookRate), -1f, 1f) * 0.25f + Noise(0.11f, 13f) * 0.25f * idle);
            _lean = Vector2.Lerp(_lean, leanTarget, 1f - Mathf.Exp(-3f * deltaTime));
            var offset = drift + new Vector2(0f, -0.0015f * jaw + 0.0015f * (breath - 0.5f));
            if (input.Boosting) offset += new Vector2(Noise(30f, 21f), Noise(34f, 27f)) * 0.0005f;
            if (_shake > 0.01f) offset += new Vector2(Noise(40f, 31f), Noise(43f, 37f)) * look.HitShake * _shake;

            _material.SetVector(Pose0Id, new Vector4(offset.x, offset.y, _lean.x, _lean.y));
            _material.SetVector(Pose1Id, new Vector4(breath, jaw, blink, Mathf.Clamp01(Mathf.Max(_glitch, input.Glitch))));
            _material.SetColor(AccentId, (UiColour ? look.HudColour : _pilot.Accent).linear);
            _material.SetVector(StyleId, new Vector4(UiColour ? look.Tint : look.SelectTint, look.Scanlines, look.ScanlineCount, look.LightAmount));
            _material.SetVector(LightShapeId, new Vector4(look.SunBoost, look.LightContrast, 0f, 0f));
            _material.SetVector(FxId, new Vector4(_time, _flash, look.Rim, 1f));
            _material.SetFloat(FlipYId, 0f);
            ApplyLight(input.Facing, deltaTime);
            Draw();
        }

        /// <summary>-1..1 smooth noise for this pilot (each pilot moves a little differently).</summary>
        private float Noise(float rate, float channel) => Mathf.PerlinNoise(_time * rate, _seed + channel) * 2f - 1f;

        /// <summary>
        /// The sun and the ambient light in portrait space: x = the picture's right (the pilot's left), y = up,
        /// z = out of the picture (where the pilot looks).
        /// </summary>
        private void ApplyLight(Quaternion facing, float deltaTime)
        {
            _sunSearch -= deltaTime;
            if (_sun == null || !_sun.isActiveAndEnabled || _sunSearch <= 0f)
            {
                _sunSearch = SunSearchSeconds;
                _sun = FindSun();
            }

            var inverse = Quaternion.Inverse(facing);
            var exposure = _look.Exposure;
            if (_sun != null)
            {
                var local = inverse * -_sun.transform.forward;
                _material.SetVector(LightDirId, new Vector4(-local.x, local.y, local.z, 0f));
                _material.SetColor(LightColorId, _sun.color.linear * (_sun.intensity * exposure));
            }
            else _material.SetColor(LightColorId, Color.black);

            _ambientDirections[0] = facing * Vector3.up;
            _ambientDirections[1] = facing * Vector3.forward;
            _ambientDirections[2] = facing * Vector3.down;
            RenderSettings.ambientProbe.Evaluate(_ambientDirections, _ambientColours);
            _material.SetColor(AmbientUpId, _ambientColours[0] * exposure);
            _material.SetColor(AmbientMidId, _ambientColours[1] * exposure);
            _material.SetColor(AmbientDownId, _ambientColours[2] * exposure);

            // The cockpit screens: accent light from below and in front.
            _material.SetVector(FillDirId, new Vector4(0f, -0.45f, 1f, 0f));
            _material.SetColor(FillColorId, (UiColour ? _look.HudColour : _pilot.Accent).linear * _look.ScreenGlow);
        }

        /// <summary>The scene's sun, else its brightest directional light.</summary>
        private static Light FindSun()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled) return RenderSettings.sun;
            Light best = null;
            foreach (var light in Object.FindObjectsByType<Light>())
                if (light.type == LightType.Directional && light.isActiveAndEnabled && (best == null || light.intensity > best.intensity))
                    best = light;
            return best;
        }

        private void Draw()
        {
            _commands.Clear();
            _commands.SetRenderTarget(Texture);
            _commands.ClearRenderTarget(false, true, Color.clear);
            _commands.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(_commands);
        }

        public void Dispose()
        {
            _commands.Release();
            CoreUtils.Destroy(_material);
            if (Texture == null) return;
            Texture.Release();
            CoreUtils.Destroy(Texture);
        }
    }
}
