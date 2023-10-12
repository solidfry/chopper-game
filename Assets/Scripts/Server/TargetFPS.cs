using UnityEngine;

namespace Server
{
    public class TargetFPS : MonoBehaviour
    {
        [SerializeField] private int targetFPS = 60;
        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFPS;
        }
    }
}