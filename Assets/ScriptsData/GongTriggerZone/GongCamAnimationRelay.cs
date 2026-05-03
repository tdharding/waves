using UnityEngine;

public class GongCamAnimationRelay : MonoBehaviour
{
    [Header("SisterNom Animator (for Gong2 sequence)")]
    public Animator sisterNomAnimator;

    [Header("Trigger name on SisterNom Animator")]
    public string gong2TriggerName = "Gong2";

    // MUST MATCH THE ANIMATION EVENT NAME EXACTLY
    public void GongEvent()
    {
        if (sisterNomAnimator != null)
            sisterNomAnimator.SetTrigger(gong2TriggerName);
        else
            Debug.LogError("[GongCamAnimationRelay] No SisterNom animator assigned!");
    }
}
