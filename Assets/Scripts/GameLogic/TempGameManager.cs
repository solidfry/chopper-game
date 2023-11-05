using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Enemies;
using Interactions;
using UnityEngine;
using UnityEngine.SceneManagement;

// TODO: This script is temporary and will be removed once after first playtest
namespace GameLogic
{
    public class TempGameManager : MonoBehaviour
    {
        [SerializeField] private List<MoveTarget> targets;
        [SerializeField] private int scoreToWin;
        [SerializeField] private int score;
        [SerializeField] private int endScreenSceneID;
        Coroutine endGameRoutine;
    
        
    }
}
