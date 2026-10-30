using System;
using System.Threading;
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

        public SignInOnStart(IAuthService auth)
        {
            _auth = auth;
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
            }
        }
    }
}
