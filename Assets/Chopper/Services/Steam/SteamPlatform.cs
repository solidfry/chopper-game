using System;
using System.Threading;
using Chopper.Services.Platform;
using Cysharp.Threading.Tasks;
using Steamworks;
using UnityEngine;
using VContainer.Unity;

namespace Chopper.Services.Steam
{
    /// <summary>
    /// Steam through Steamworks.NET. Gives a Web API ticket for the Unity Authentication Steam provider.
    /// Only compiles when the Steamworks.NET package is installed (define <c>CHOPPER_STEAM</c>).
    /// Register as a singleton in the root scope; VContainer calls <see cref="Tick"/> (Steam callbacks) and
    /// <see cref="Dispose"/> (shutdown).
    /// </summary>
    public sealed class SteamPlatform : IPlatformIdentity, ITickable, IDisposable
    {
        /// <summary>Must match the identity set for the Steam provider in the Unity Dashboard.</summary>
        public const string UnityAuthIdentity = "unityauthenticationservice";

        private readonly bool _initialised;

        public SteamPlatform()
        {
            try
            {
                _initialised = SteamAPI.Init();
            }
            catch (DllNotFoundException e)
            {
                Debug.LogWarning($"Steam not available: {e.Message}");
            }
            if (!_initialised)
                Debug.LogWarning("Steam is not running. Signing in without Steam.");
        }

        public bool IsAvailable => _initialised;
        public string DisplayName => _initialised ? SteamFriends.GetPersonaName() : "Pilot";

        public async UniTask<PlatformTicket?> GetSignInTicketAsync(CancellationToken cancellationToken)
        {
            if (!_initialised) return null;

            var completion = new UniTaskCompletionSource<PlatformTicket?>();
            using var callback = Callback<GetTicketForWebApiResponse_t>.Create(response =>
            {
                if (response.m_eResult != EResult.k_EResultOK)
                {
                    Debug.LogWarning($"Steam ticket failed: {response.m_eResult}");
                    completion.TrySetResult(null);
                    return;
                }

                var bytes = new byte[response.m_cubTicket];
                Array.Copy(response.m_rgubTicket, bytes, bytes.Length);
                completion.TrySetResult(new PlatformTicket(BitConverter.ToString(bytes).Replace("-", string.Empty), UnityAuthIdentity));
            });

            SteamUser.GetAuthTicketForWebApi(UnityAuthIdentity);
            using (cancellationToken.Register(() => completion.TrySetCanceled()))
                return await completion.Task;
        }

        public void Tick()
        {
            if (_initialised)
                SteamAPI.RunCallbacks();
        }

        public void Dispose()
        {
            if (_initialised)
                SteamAPI.Shutdown();
        }
    }
}
