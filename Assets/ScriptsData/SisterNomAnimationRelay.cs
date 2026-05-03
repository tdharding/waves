using UnityEngine;

public class SisterNomAnimationRelay : MonoBehaviour
{
 
    // -----------------------------------------
    // NEW — USED FOR SECOND GONG SEQUENCE ONLY
    // -----------------------------------------
    [Header("Second Gong (GongWavesController)")]
    public GongWavesController gongController;

    // Called by animation event in Gong2 animation
    public void SecondGong_BellSoundPlay()
    {
        if (gongController != null)
            gongController.PlayGongSound();
    }

    // Called by animation event at end of Gong2 animation
    public void SecondGong_AnimationFinished()
    {
        if (gongController != null)
            gongController.OnSecondGongSequenceFinished();
    }
}
