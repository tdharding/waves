using UnityEngine;

[CreateAssetMenu(fileName = "EnemyProfile", menuName = "Levels/Enemy Profile")]
public class EnemyProfile : ScriptableObject
{
    [Tooltip("The enemy prefab to spawn. Null = no enemy.")]
    public GameObject prefab;

    [Tooltip("How many level completions before this enemy appears. 1 = second visit.")]
    public int spawnOnVisit = 1;
}
