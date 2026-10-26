using System.Threading;
using Cysharp.Threading.Tasks;

namespace Chopper.Services.Platform
{
    /// <summary>A sign-in ticket from the platform (Steam), for Unity Authentication.</summary>
    public readonly struct PlatformTicket
    {
        public string Ticket { get; }
        /// <summary>The identity the ticket was made for; Unity Authentication checks it.</summary>
        public string Identity { get; }

        public PlatformTicket(string ticket, string identity)
        {
            Ticket = ticket;
            Identity = identity;
        }
    }

    /// <summary>The store platform the game runs on (Steam, or none in the editor / dev builds).</summary>
    public interface IPlatformIdentity
    {
        /// <summary>False when the platform is missing (Steam not running): sign in anonymously instead.</summary>
        bool IsAvailable { get; }
        string DisplayName { get; }

        /// <summary>Returns null when <see cref="IsAvailable"/> is false or the platform gives no ticket.</summary>
        UniTask<PlatformTicket?> GetSignInTicketAsync(CancellationToken cancellationToken);
    }

    /// <summary>No store platform. Editor, dev builds and Multiplayer Play Mode clones use this.</summary>
    public sealed class OfflinePlatform : IPlatformIdentity
    {
        public bool IsAvailable => false;
        public string DisplayName => "Pilot";

        public UniTask<PlatformTicket?> GetSignInTicketAsync(CancellationToken cancellationToken)
            => UniTask.FromResult<PlatformTicket?>(null);
    }
}
