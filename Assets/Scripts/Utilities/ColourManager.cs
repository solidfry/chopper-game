using UnityEngine;

namespace Utilities
{
    public class ColourManager : MonoBehaviour
    {
        // SingletonClass 
        private static ColourManager _instance;
        public static ColourManager Instance => _instance;
    
        [SerializeField] ColorData colours;
    
        private void Start()
        {
            if (_instance != null && _instance != this) Destroy(gameObject);
            else _instance = this;
        }
    
    }
}
