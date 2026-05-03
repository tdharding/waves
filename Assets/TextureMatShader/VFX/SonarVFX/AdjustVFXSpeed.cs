using UnityEngine;
using UnityEngine.VFX;

public class AdjustVFXSpeed : MonoBehaviour
{
    [SerializeField] float timeScale = 1.0f;
    [SerializeField] VisualEffect VFX;

    void Start()
    {
        VFX = GetComponent<VisualEffect>();
    }

    void Update()
    {
        VFX.playRate = timeScale;
    }
}