using UnityEngine;
using UnityEngine.UI;

public class PlayerAvatarImage : MonoBehaviour
{
    [SerializeField] Texture defaultAvatar;
    [SerializeField] RawImage avatarImage;

    private void Awake()
    {
        Initialise();
    }

    private void Initialise()
    {
        if(defaultAvatar == null) return;
        
        SetAvatar(defaultAvatar);
    }
    
    public void SetAvatar(Texture texture)
    {
        if(texture != null)
            avatarImage.texture = texture;
        else
        {
            avatarImage.texture = defaultAvatar;
        }
    }
    
}
