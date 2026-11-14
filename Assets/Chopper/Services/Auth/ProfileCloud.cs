using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Services.CloudCode;

namespace Chopper.Services.Auth
{
    /// <summary>The signed-in player's progression record on the server (Cloud Save, read through Cloud Code).</summary>
    public interface IProfileCloud
    {
        /// <summary>The record as JSON text; "" when the player has none yet. Throws when the call fails.</summary>
        UniTask<string> LoadMineAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Calls <c>Progress_Mine</c> of the Cloud Code module (<c>CloudCode/ChopperAllocator/Project/ProgressStore.cs</c>).
    /// Needs a signed-in player. Written without the editor (14 Nov 2026).
    /// </summary>
    public sealed class UnityProfileCloud : IProfileCloud
    {
        private const string Module = "ChopperAllocator";
        private const string Function = "Progress_Mine";

        public async UniTask<string> LoadMineAsync(CancellationToken cancellationToken)
        {
            var record = await CloudCodeService.Instance.CallModuleEndpointAsync<string>(Module, Function, null)
                .AsUniTask().AttachExternalCancellation(cancellationToken);
            return record ?? "";
        }
    }
}
