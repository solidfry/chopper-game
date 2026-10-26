using System.Threading;
using Cysharp.Threading.Tasks;

namespace Chopper.Services.Auth
{
    /// <summary>Signs the player in to Unity Gaming Services (needed for matchmaking, save, leaderboards).</summary>
    public interface IAuthService
    {
        bool IsSignedIn { get; }
        /// <summary>Unity player id, or null before sign-in.</summary>
        string PlayerId { get; }
        string DisplayName { get; }

        /// <summary>No login screen: Steam ticket when Steam runs, anonymous otherwise. Safe to call again.</summary>
        UniTask SignInAsync(CancellationToken cancellationToken);
    }
}
