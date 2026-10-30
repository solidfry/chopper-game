using System;
using System.Threading;
using Chopper.Services.Platform;
using Cysharp.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using UnityEngine;

namespace Chopper.Services.Auth
{
    /// <summary>
    /// Unity Authentication: Steam provider when the platform gives a ticket and the dashboard accepts it; anonymous
    /// otherwise (no Steam, or the Steam provider not set up for this App ID).
    /// </summary>
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
                await UnityServices.InitializeAsync(new InitializationOptions().SetEnvironmentName(ServiceEnvironment.Current))
                    .AsUniTask().AttachExternalCancellation(cancellationToken);
            if (AuthenticationService.Instance.IsSignedIn) return;

            var ticket = await _platform.GetSignInTicketAsync(cancellationToken);
            if (ticket.HasValue)
            {
                try
                {
                    await AuthenticationService.Instance
                        .SignInWithSteamAsync(ticket.Value.Ticket, ticket.Value.Identity)
                        .AsUniTask().AttachExternalCancellation(cancellationToken);
                    return;
                }
                catch (Exception e) when (e is AuthenticationException or RequestFailedException)
                {
                    // The Steam provider is not set up for this App ID yet (the Spacewar test ID 480 never is):
                    // play on with an anonymous account.
                    Debug.LogWarning($"Steam sign-in refused ({e.Message}). Signing in anonymously.");
                }
            }

            await AuthenticationService.Instance.SignInAnonymouslyAsync()
                .AsUniTask().AttachExternalCancellation(cancellationToken);
        }
    }
}
