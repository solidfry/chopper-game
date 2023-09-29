using Effects.Structs;
using Enums;
using UnityEngine;

namespace Events
{
    public static class GameEvents
    {
        #region User Events

        public delegate void Pause();
        public delegate void UnPause();
        public static Pause onPauseGame;
        public static UnPause onUnPauseGame;

        #endregion

        #region Player Events
        public delegate void PlayerKill();
        public delegate void PlayerTakeDamage(int amount);
        public delegate void PlayerHeal(int amount);
        public delegate void PlayerDied();
        public delegate void PlayerRespawn(float delaySeconds = 0, Transform positionToRespawn = null);
        public delegate void PlayerFreeze();

        public static PlayerHeal onPlayerHealedEvent;
        public static PlayerKill onPlayerKillEvent;
        public static PlayerDied onPlayerDiedEvent;
        public static PlayerRespawn onPlayerRespawnEvent;
        public static PlayerFreeze onPlayerFreezeEvent;
        #endregion

        // #region Utility Events
        //
        //
        // #endregion
        //
        // #region Music Events
        //
        // #endregion

        #region UI Events

        public delegate void SetValue(int value);
        public delegate void PlayerHealthUIChange(float normalisedCurrentHealth);
        public delegate void PlayerManaUIChange(float normalisedCurrentMana);
        public delegate void PlayerTimerUIChange(float normalisedCurrentTimer);
        public delegate void BossHealthUIChange(float normalisedCurrentHealth);

        public delegate void TargetEnemy();

        public static TargetEnemy onTargetEnemyEvent;
        public static SetValue onSetHealthCountEvent;
        public static PlayerHealthUIChange onPlayerHealthUIChangeEvent;




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
