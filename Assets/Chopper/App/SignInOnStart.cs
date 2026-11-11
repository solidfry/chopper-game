using System;
using System.Threading;
using Chopper.Presentation.Flow;
using Chopper.Services.Auth;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer.Unity;

namespace Chopper.App
{
    /// <summary>Signs in as soon as the game starts, so the menu never shows a login screen.</summary>
    public sealed class SignInOnStart : IAsyncStartable
    {
        private readonly IAuthService _auth;
        private readonly NotificationFeed _notices;

        public SignInOnStart(IAuthService auth, NotificationFeed notices)
        {
            _auth = auth;
            _notices = notices;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            try
            {
                await _auth.SignInAsync(cancellation);
                Debug.Log($"Signed in as {_auth.DisplayName} ({_auth.PlayerId}), environment {Chopper.Services.ServiceEnvironment.Current}.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                // Offline play (training) still works; online menus check IsSignedIn and offer a retry.
                Debug.LogWarning($"Sign-in failed: {e.Message}");
                _notices?.Post("Offline mode", "Sign-in failed. Training works; online play needs a connection.", NoticeKind.Warning, 7f);
            }
        }
    }
}
