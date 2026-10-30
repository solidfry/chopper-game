using UnityEngine;

namespace Chopper.Services
{
    /// <summary>
    /// The Unity Gaming Services environment the game uses (Dashboard → Environments). The editor and development builds
    /// play against <see cref="Development"/>, release builds against <see cref="Production"/>, so testing never
    /// touches the live queues, Cloud Code and data. Each environment needs its own matchmaker queue and Cloud Code
    /// module (and the Edgegap secret, unless it is project-wide).
    /// </summary>
    public static class ServiceEnvironment
    {
        public const string Development = "development";
        public const string Production = "production";

        /// <summary>Development in the editor and in development builds (<see cref="Debug.isDebugBuild"/>).</summary>
        public static string Current => Debug.isDebugBuild ? Development : Production;
    }
}
