using System.Threading;
using Chopper.Services.Platform;
using Cysharp.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace Chopper.Services.Auth
{
    /// <summary>Unity Authentication: Steam provider when the platform gives a ticket, anonymous otherwise.</summary>
    public sealed class UnityAuthService : IAuthService
    {
        private readonly IPlatformIdentity _platform;

        public UnityAuthService(IPlatformIdentity platform)
        {
            _platform = platform;
        }

        public bool IsSignedIn => UnityServices.State == ServicesInitializationState.Initialized &&
                                  AuthenticationService.Instance.IsSignedIn;

        public string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : null;
        public string DisplayName => _platform.DisplayName;

        public async UniTask SignInAsync(CancellationToken cancellationToken)
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync().AsUniTask().AttachExternalCancellation(cancellationToken);
            if (AuthenticationService.Instance.IsSignedIn) return;

            var ticket = await _platform.GetSignInTicketAsync(cancellationToken);
            if (ticket.HasValue)
            {
                await AuthenticationService.Instance
                    .SignInWithSteamAsync(ticket.Value.Ticket, ticket.Value.Identity)
                    .AsUniTask().AttachExternalCancellation(cancellationToken);
            }
            else
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync()
                    .AsUniTask().AttachExternalCancellation(cancellationToken);
            }
        }
    }
}
