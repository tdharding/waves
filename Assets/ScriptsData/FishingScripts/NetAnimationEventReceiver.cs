using UnityEngine;

public class NetAnimationEventReceiver : MonoBehaviour
{
    [SerializeField] private WhirlFXController whirlFX;

    public void SetWhirlFX(WhirlFXController fx) => whirlFX = fx;

    // Called by the DeployNetBoat animation event
    public void OnDeployNetComplete()
    {
        whirlFX?.OnDeployNetComplete();
    }

    // Called by the RetractNetBoat animation event (last frame)
    public void OnRetractNetComplete()
    {
        whirlFX?.OnRetractNetComplete();
    }
}