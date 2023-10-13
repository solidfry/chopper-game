using System.Collections.Generic;
using Networking.Spawns;
using UnityEngine;

namespace Networking
{
    public class ServerSpawnManager : SingletonNetwork<ServerSpawnManager>
    {
        [SerializeField] List<TeamSpawnLocations> teamSpawnLocations = new();

        public Transform UseSpawnLocation()
        {
            foreach (var team in teamSpawnLocations)
            {
                var tr = team.GetNextUnusedPosition();
                if (tr != null)
                    return tr;
            }

            return null;
        }

        public void ReleaseSpawnLocationInTeamAtIndex(int teamIndex, int index) => teamSpawnLocations[teamIndex].ReleasePositionAtIndex(index);

        public void ReleaseSpawnLocation(Transform transform) => teamSpawnLocations.ForEach(team => team.ReleasePositionByTransform(transform));

        public void ReleaseAllSpawnLocations() => teamSpawnLocations.ForEach(team => team.ReleaseAllPositions());

        public bool AllSpawnLocationsUsed() => teamSpawnLocations.TrueForAll(team => team.AllPositionsUsed());

        public bool AllSpawnLocationsUsedInTeam(int teamIndex) => teamSpawnLocations[teamIndex].AllPositionsUsed();


    }

}
