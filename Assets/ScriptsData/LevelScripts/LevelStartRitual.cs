using UnityEngine;
using System;
using System.Collections;

[CreateAssetMenu(
    fileName = "LevelStartRitual",
    menuName = "Levels/Start Ritual"
)]
public class LevelStartRitual : ScriptableObject
{
    [Header("Maze Presentation")]
    [SerializeField] private bool animateMaze = false;
    [SerializeField] private float revealDelay = 0f;

    public IEnumerator Play(LevelSpawner levelSpawner, Action onComplete)
    {
        if (revealDelay > 0f)
            yield return new WaitForSeconds(revealDelay);

        if (levelSpawner != null)
        {
            if (animateMaze)
                levelSpawner.RevealMaze();
            else
                levelSpawner.RevealMazeInstant();
        }

     Debug.Log("levelstartritualcomplete");
        onComplete?.Invoke();
    
    }
}
