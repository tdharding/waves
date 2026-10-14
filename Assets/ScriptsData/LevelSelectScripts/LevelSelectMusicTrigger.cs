using UnityEngine;

/// <summary>
/// The box across the river at a node with Audio on. The boat sailing into it hands the node to
/// <see cref="LevelSelectMusicController"/>, which crossfades into that node's music.
///
/// Built by the Level Select Designer on Generate (and when a node's Audio is switched), sized
/// off the river at the node — never placed or sized by hand.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class LevelSelectMusicTrigger : MonoBehaviour
{
    [Tooltip("The designer node whose music this box starts. Written by the designer.")]
    public string nodeId;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("BoatPrefab")) return;

        var music = LevelSelectMusicController.Instance;
        if (music == null)
        {
            Debug.LogWarning($"[LevelSelectMusic] Boat entered node {nodeId}'s box, but there is no " +
                             "LevelSelectMusicController in the scene.", this);
            return;
        }
        music.BoatEntered(nodeId);
    }
}
