using Effects.Structs;
using Enums;
using UnityEngine;

namespace Events
{
    public static class GameEvents
    {

        #region Player Events
        public delegate void PlayerKill(ulong clientId);
        public delegate void PlayerTakeDamage(int amount);
        public delegate void PlayerHeal(int amount);
        public delegate void PlayerDied(ulong clientId);
        public delegate void PlayerRespawn(float delaySeconds = 0, Transform positionToRespawn = null);
        public delegate void PlayerFreeze(ulong clientId);
        public delegate void PlayerFreezeAll();
        public delegate void PlayerUnFreezeAll();

        public static PlayerHeal OnPlayerHealedEvent;
        public static PlayerKill OnPlayerKillEvent;
        public static PlayerDied OnPlayerDiedEvent;
        public static PlayerRespawn OnPlayerRespawnEvent;
        public static PlayerFreezeAll OnPlayerFreezeAllAllEvent;
        public static PlayerUnFreezeAll OnPlayerUnFreezeAllAllEvent;
        #endregion

        #region UI Events
        public delegate void PlayAudioClip(string soundListName);
        public static PlayAudioClip OnPlayRandomUISoundEvent;
        #endregion

        #region Server Events
        public delegate void StartMatch();
        public delegate void EndMatch();
        public static StartMatch OnStartMatchEvent;
        public static EndMatch OnEndMatchEvent;
        
        public delegate void Notification(string message);
        public static Notification OnNotificationEvent;

        #endregion

        #region Effects
        public delegate void SceneTransitionIn();
        public delegate void SceneTransitionStart();
        public delegate void SceneTransitionOut();
        public delegate void SceneTransitionEnd();
        public delegate void ScreenShake(Strength str, float lengthInSeconds = 0.2f);
        public delegate void ParticleEffect(ParticleEvent particleEvent);
        public delegate void LoadLevel();
        public delegate void SendPlayer(Transform player);
        public delegate void SendCamera(Camera camera);

        public static ScreenShake onScreenShakeEvent;
        public static SceneTransitionOut onSceneTransitionOutEvent;
        public static SceneTransitionStart onSceneTransitionStartEvent;
        public static SceneTransitionIn onSceneTransitionInEvent;
        public static SceneTransitionEnd onSceneTransitionEndEvent;
        public static SendPlayer onSendPlayerEvent;
        public static SendCamera onSendCameraEvent;
        public static ParticleEffect onParticleEvent;
        public static LoadLevel onLevelLoadEvent;
        #endregion
    }
}
