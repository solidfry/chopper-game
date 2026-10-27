using UnityEngine;

namespace Chopper.Simulation.Loadout
{
    /// <summary>
    /// Marks where a module snaps onto a chassis ("lego" socket). Put on a child of the chassis ghost prefab; its
    /// position and rotation are the attach point (module prefab pivots go here). Slot order on the chassis definition
    /// maps to sockets by type and index: the second Pylon slot uses the Pylon socket with index 1.
    /// The hangar focus pose says where its camera goes when this socket is selected.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SocketAuthoring : MonoBehaviour
    {
        [SerializeField] private SlotType slot;
        [SerializeField, Tooltip("0 = first socket of this type, 1 = second, ...")] private int index;

        [Header("Hangar focus")]
        [SerializeField, Tooltip("Camera position relative to this socket when it is selected in the hangar.")]
        private Vector3 focusCameraOffset = new(0f, 1.5f, -4f);
        [SerializeField] private float focusFieldOfView = 35f;
        [SerializeField, Tooltip("Turntable yaw (degrees) that shows this socket best.")]
        private float focusChopperYaw;

        public SlotType Slot => slot;
        public int Index => index;
        public Vector3 FocusCameraOffset => focusCameraOffset;
        public float FocusFieldOfView => focusFieldOfView;
        public float FocusChopperYaw => focusChopperYaw;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(SlotType slotType, int socketIndex, float yaw)
        {
            slot = slotType;
            index = socketIndex;
            focusChopperYaw = yaw;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.22f, 1f, 0.53f, 0.9f);
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.3f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.6f);
        }
    }
}
