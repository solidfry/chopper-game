using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace OccaSoftware.Buto.Runtime
{
    [ExecuteAlways]
    [AddComponentMenu("OccaSoftware/Buto/Buto Light")]
    [RequireComponent(typeof(Light))]
    public sealed class ButoLight : ButoPlaceableObject
    {
        [SerializeField]
        private bool inheritDataFromLightComponent = false;

        [SerializeField]
        private Light lightComponent = null;

        [SerializeField]
        [ColorUsage(false, false)]
        private Color lightColor = Color.white;

        public Vector4 LightColor
        {
            get
            {
                if (inheritDataFromLightComponent && lightComponent != null)
                    return lightComponent.color;

                return lightColor;
            }
        }

        [SerializeField]
        [Min(0)]
        private float lightIntensity = 10f;
        public float LightIntensity
        {
            get
            {
                if (inheritDataFromLightComponent && lightComponent != null)
                    return lightComponent.intensity;

                return lightIntensity;
            }
        }

        public Vector4 LightDirection
        {
            get { return -transform.localToWorldMatrix.GetColumn(2); }
        }

        public Vector4 LightPosition
        {
            get { return transform.localToWorldMatrix.GetColumn(3); }
        }

        private Vector4 RecalculateLightAngles()
        {
            if (lightComponent.type == LightType.Spot)
            {
                float innerHalfRads = Mathf.Cos(0.5f * lightComponent.innerSpotAngle * Mathf.Deg2Rad);
                float outerHalfRads = Mathf.Cos(0.5f * lightComponent.spotAngle * Mathf.Deg2Rad);
                float mapped = 1.0f / Mathf.Max(innerHalfRads - outerHalfRads, 0.001f);
                return new Vector4(mapped, mapped * -outerHalfRads, 0f, 0f);
            }
            else
            {
                return new Vector4(0f, 1f, 0f, 0f);
            }
        }

        private Vector4 lightAngles;

        private int GetHash(int a, float b, float c)
        {
            int hash = 17;
            hash = hash * 23 + a.GetHashCode();
            hash = hash * 23 + b.GetHashCode();
            hash = hash * 23 + c.GetHashCode();
            return hash;
        }

        private int lightHash;

        public Vector4 GetLightAngles()
        {
            int hash = GetHash((int)lightComponent.type, lightComponent.innerSpotAngle, lightComponent.spotAngle);

            if (lightHash != hash)
            {
                lightHash = hash;
                lightAngles = RecalculateLightAngles();
            }
            return lightAngles;
        }

        public static void SortByDistance(Vector3 c)
        {
            _Lights = _Lights.OrderBy(x => x.GetSqrMagnitude(c)).ToList();
        }

        private static List<ButoLight> _Lights = new List<ButoLight>();
        public static List<ButoLight> Lights
        {
            get { return _Lights; }
        }

        protected override void Reset()
        {
            ButoCommon.CheckMaxLightCount(Lights.Count, this);
        }

        private void OnValidate()
        {
            lightComponent = GetComponent<Light>();
        }

        protected override void OnEnable()
        {
            lightComponent = GetComponent<Light>();
            _Lights.Add(this);
        }

        protected override void OnDisable()
        {
            _Lights.Remove(this);
        }

        public void CheckForLight()
        {
            lightComponent = GetComponent<Light>();
        }

        public void SetInheritance(bool state)
        {
            inheritDataFromLightComponent = state;
        }

        #region Editor
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!inheritDataFromLightComponent)
            {
                Gizmos.color = LightColor;
                Gizmos.DrawWireSphere(transform.position, lightIntensity);
            }
        }
#endif
        #endregion
    }
}
