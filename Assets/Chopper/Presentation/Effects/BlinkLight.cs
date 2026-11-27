using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// A warning blink (air mine, Docs/WeaponFeelPlan.md step 3): the light and an optional glow object flash on and off.
    /// Changes intensity and the glow's active state only, never <see cref="Behaviour.enabled"/>, so the effect pool can
    /// still switch the light off when it releases the round.
    /// </summary>
    public sealed class BlinkLight : MonoBehaviour
    {
        [SerializeField] private Light lightSource;
        [SerializeField, Tooltip("Shown while the light is on (a small emissive mesh). Optional.")] private GameObject glow;
        [SerializeField, Min(0.1f), Tooltip("Blinks per second.")] private float rate = 2.5f;
        [SerializeField, Range(0.05f, 0.95f), Tooltip("Share of each blink the light is on.")] private float onShare = 0.3f;
        [SerializeField, Min(0f)] private float intensity = 6f;

        private float _start;

        private void OnEnable() => _start = Time.time;

        private void Update()
        {
            var on = Mathf.Repeat((Time.time - _start) * rate, 1f) < onShare;
            if (lightSource != null) lightSource.intensity = on ? intensity : 0f;
            if (glow != null && glow.activeSelf != on) glow.SetActive(on);
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(Light source, GameObject glowObject, float blinksPerSecond, float lit)
        {
            lightSource = source;
            glow = glowObject;
            rate = blinksPerSecond;
            intensity = lit;
        }
    }
}
