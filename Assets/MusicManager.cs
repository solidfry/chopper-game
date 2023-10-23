
using UnityEngine;

public class MusicManager : SingletonPersistent<MusicManager>
{
    AudioSource _audioSource;
    public override void Awake()
    {
        base.Awake();
        _audioSource = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
    }
    
    private void OnDisable()
    {
    }
}
