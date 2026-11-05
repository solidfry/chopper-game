using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// The dedicated server's first scene (<c>Assets/Scenes/ServerBoot.unity</c>, made by "Build Linux server"):
    /// picks the match map and loads it. <c>-map NAME</c> (scene name, e.g. Map_City04, or map id) on the command line
    /// picks one; else a random map from the <see cref="MapCatalog"/> match pool. Each client hears the map on connect
    /// (<see cref="MatchMapAnnounceSystem"/>) and loads the same scene.
    /// Planned: the matchmaker decides the map (a match property) and passes it as <c>-map</c>; random is the fallback.
    /// </summary>
    public sealed class ServerMapLoader : MonoBehaviour
    {
        [SerializeField] private MapCatalog catalog;

        /// <summary>Editor only: used by the server build.</summary>
        public void SetCatalog(MapCatalog maps) => catalog = maps;

        private void Start()
        {
            if (catalog == null)
            {
                Debug.LogError("[Server] No map catalog on the boot scene: no map to load.");
                return;
            }
            var asked = ServerArgs.String("-map");
            var map = string.IsNullOrEmpty(asked) ? null
                : byte.TryParse(asked, out var id) ? catalog.Find(id) : catalog.FindScene(asked);
            if (!string.IsNullOrEmpty(asked) && map == null) Debug.LogWarning($"[Server] -map {asked} is not in the catalog; random map.");
            map ??= catalog.RandomMatchMap(Random.value);
            if (map == null)
            {
                Debug.LogError("[Server] The map catalog has no match map.");
                return;
            }
            Debug.Log($"[Server] Map {map.Id} {map.DisplayName} ({map.Scene}).");
            SceneManager.LoadScene(map.Scene);
        }
    }
}
