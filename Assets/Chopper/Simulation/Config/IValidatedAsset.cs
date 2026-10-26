using System.Collections.Generic;

namespace Chopper.Simulation.Config
{
    /// <summary>
    /// A config or definition asset that can check itself. The Chopper Setup window collects the problems of every
    /// asset in one list.
    /// </summary>
    public interface IValidatedAsset
    {
        /// <summary>Adds one plain sentence per problem. Adds nothing when the asset is fine.</summary>
        void CollectProblems(List<string> problems);
    }
}
