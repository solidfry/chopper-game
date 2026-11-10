using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Simulation.Loadout
{
    /// <summary>
    /// The points of a pilot's portrait that the portrait shader moves (the "rig", <c>Chopper/PilotPortrait</c>): the
    /// head turns on the neck, the jaw opens with the voice, the eyes blink, the chest breathes. All points are parts of
    /// the image (0..1), from the top left corner, as a paint program shows them. Set by Chopper → Setup, step 1, for
    /// the starting pilots; change them here when the art changes.
    /// </summary>
    [Serializable]
    public sealed class PilotFace
    {
        [Tooltip("Tangent space normal map of the portrait (same size). The scene light uses it. Empty = flat (the art only).")]
        public Texture2D NormalMap;
        [Tooltip("Middle of the head (with hair or helmet).")]
        public Vector2 HeadCentre = new(0.5f, 0.25f);
        [Tooltip("Half the head's width and height.")]
        public Vector2 HeadSize = new(0.2f, 0.24f);
        [Tooltip("Base of the neck: the head turns round this point. Below it is the chest (it breathes).")]
        public Vector2 Neck = new(0.5f, 0.5f);
        [Tooltip("Middle of the image's left eye. (0, 0) = no blink on this eye (a lens, a visor).")]
        public Vector2 LeftEye = new(0.42f, 0.29f);
        [Tooltip("Middle of the image's right eye. (0, 0) = no blink on this eye.")]
        public Vector2 RightEye = new(0.6f, 0.29f);
        [Tooltip("Half an eye's width.")]
        public float EyeSize = 0.045f;
        [Tooltip("Middle of the mouth (where the lips meet).")]
        public Vector2 Mouth = new(0.5f, 0.42f);
        [Tooltip("Half the mouth's width. 0 = the mouth is covered (a mask): the voice lights the mask instead.")]
        public float MouthWidth = 0.06f;
        [Tooltip("Bottom of the chin (height only).")]
        public float Chin = 0.47f;

        public bool HasLeftEye => LeftEye != Vector2.zero;
        public bool HasRightEye => RightEye != Vector2.zero;
        public bool Masked => MouthWidth <= 0f;

        public void CollectProblems(string owner, List<string> problems)
        {
            if (Neck.y <= HeadCentre.y) problems.Add($"{owner}: face neck must be below the head centre.");
            if (Chin <= Mouth.y) problems.Add($"{owner}: face chin must be below the mouth.");
            if (HeadSize.x <= 0f || HeadSize.y <= 0f) problems.Add($"{owner}: face head size must be above 0.");
        }
    }
}
