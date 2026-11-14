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
        private readonly IProfileCloud _profileCloud;

        public SignInOnStart(IAuthService auth, NotificationFeed notices, IProfileCloud profileCloud)
        {
            _profileCloud = profileCloud;
            _auth = auth;
            _notices = notices;
        }

        /// <summary>
        /// The server's record replaces the local save (the screens read the local save). No record on the server yet
        /// (no online match played): the local save stays. A failure only logs: the local save is used.
        /// </summary>
        private async UniTask MirrorProfile(CancellationToken cancellation)
        {
            try
            {
                var json = await _profileCloud.LoadMineAsync(cancellation);
                if (string.IsNullOrEmpty(json)) return;
                var profile = JsonUtility.FromJson<Chopper.Presentation.Profile.PlayerProfile>(json);
                if (profile == null) return;
                new Chopper.Presentation.Profile.PrefsProfileStore(new Chopper.Presentation.Hangar.PlayerPrefsHangarPrefs()).Save(profile);
                Debug.Log($"Profile read from the server: {profile.totalXp} XP, {profile.matches} matches.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"The server profile could not be read ({e.Message}); the local one is used.");
            }
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            try
            {
                await _auth.SignInAsync(cancellation);
                Debug.Log($"Signed in as {_auth.DisplayName} ({_auth.PlayerId}), environment {Chopper.Services.ServiceEnvironment.Current}.");
                await MirrorProfile(cancellation);
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
