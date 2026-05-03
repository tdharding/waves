using UnityEngine;

public class NetAnimationEventReceiver : MonoBehaviour
{
    private WhirlFXController whirlFX;

    public void SetWhirlFX(WhirlFXController fx) => whirlFX = fx;

    // Called by the DeployNetBoat animation event
    public void OnDeployNetComplete()
    {
        whirlFX?.OnDeployNetComplete();
    }
}