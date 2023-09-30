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
    
        void Start()
        {
            targets = FindObjectsOfType<MoveTarget>().ToList();
            scoreToWin = targets.Count;
        }

        private void OnEnable()
        {
            Death.OnDeath += AddScore;
        }

        private void OnDisable()
        {
            Death.OnDeath -= AddScore;
        }
    
        void AddScore()
        { 
            score++;
            EndGame();
        }
    
        void EndGame()
        {
            if (score == scoreToWin)
            {
                Debug.Log("You win!");
                endGameRoutine = StartCoroutine(EndGameRoutine(endScreenSceneID));
            }
        }

        private IEnumerator EndGameRoutine(int id)
        {
            yield return new WaitForSeconds(2);
            SceneManager.LoadSceneAsync(id, LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            if(endGameRoutine != null)
                StopCoroutine(endGameRoutine);
        }
    }
}
