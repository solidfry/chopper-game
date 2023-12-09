using Effects.Structs;
using Enums;
using GameLogic.ScriptableObjects;
using Interactions;
using UnityEngine;

namespace Events
{
    public static class GameEvents
    {

        #region Player Events
        public delegate void PlayerKill(ulong clientIdOfAttacker);
        public delegate void PlayerTakeDamage(int amount);
        public delegate void PlayerDied(ulong clientId);
        public delegate void PlayerFreeze(ulong clientId);
        public delegate void PlayerFreezeAll();
        public delegate void PlayerUnFreezeAll();
        public delegate void TogglePlayerControls(bool enable);
        public static TogglePlayerControls OnTogglePlayerControlsEvent;

        public delegate void SendPlayerScore(NetworkPlayerScore playerScore);
        public static SendPlayerScore OnSendPlayerScoreEvent;
        
        public static PlayerKill OnPlayerKillEvent;
        public static PlayerDied OnPlayerDiedEvent;
        public static PlayerFreezeAll OnPlayerFreezeAllEvent;
        public static PlayerUnFreezeAll OnPlayerUnFreezeAllEvent;
        
        public delegate void ShowEndGameScreen();
        public static ShowEndGameScreen OnShowEndGameScreenEvent;
        
        public delegate void DisableMainCamera();
        public static DisableMainCamera OnDisableMainCameraEvent;
        
        public delegate void PlayerInBounds();
        public static PlayerInBounds OnPlayerInBoundsEvent;
        
        public delegate void PlayerOutOfBounds();
        public static PlayerOutOfBounds OnPlayerOutOfBoundsEvent;
        
        public delegate void PlayerOutOfBoundsDestroy(ulong clientId);
        public static PlayerOutOfBoundsDestroy OnPlayerOutOfBoundsDestroyEvent;
        
        
        #endregion

        #region UI Events
        public delegate void PlayAudioClip(string soundListName);
        public static PlayAudioClip OnPlayRandomUISoundEvent;
        public delegate void SetTimerColors(Color textColor, Color backgroundColor);
        public static SetTimerColors OnSetTimerColors;

        public delegate void UpdatePlayerScore(ulong clientId, int kills, int deaths);
        public static UpdatePlayerScore OnUpdatePlayerScoreEvent;
        
        public delegate void RemovePlayerScore(ulong clientId);
        public static RemovePlayerScore OnRemovePlayerScoreEvent;
        
        public delegate void ShowCursor();
        public static ShowCursor OnShowCursorEvent;
        
        public delegate void HideCursor();
        public static HideCursor OnHideCursorEvent;
        
        #endregion

        #region Server Events

        public delegate void PreMatch();
        public static PreMatch OnPreMatchEvent;
        public delegate void StartMatch();
        public delegate void EndMatch();
        
        public static StartMatch OnStartMatchEvent;
        public static EndMatch OnEndMatchEvent;
        
        public delegate void InProgressGame();
        public static InProgressGame OnInProgressGameEvent;

        public delegate void PostGame();
        public static PostGame OnPostMatchEvent;
        
        public delegate void TimerStart();
        public delegate void TimerEnd();
        public static TimerStart OnTimerStartEvent;
        public static TimerEnd OnTimerEndEvent;
        
        public delegate void TickTimer(float time);
        public static TickTimer OnTickTimer;
        
        public delegate void SetTimer(float time);
        public static SetTimer OnSetTimerEvent;
        
        public delegate void SendGameMode(GameMode gameMode);
        public static SendGameMode OnSendGameModeEvent;
        
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
        
        #region Audio
        public delegate void MusicChange(AudioClip clip, TrackPlayMode playMode = TrackPlayMode.PlayOnce); 
        public static MusicChange OnMusicChangedEvent;
        public delegate void MusicStop();
        public static MusicStop OnMusicStoppedEvent;
        public delegate void MusicStart();
        public static MusicStart OnMusicStartedEvent;
        #endregion

    }
}
