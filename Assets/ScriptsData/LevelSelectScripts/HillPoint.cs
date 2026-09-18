using UnityEngine;

/// <summary>
/// The shape of one hill (or hole) beyond its position, radius and height. Put on each generated
/// hill point by the Level Select Designer and read by <see cref="LandscapeTool"/> into the
/// CalculateHills shader. Authored on the designer's hill list, not here — a sync overwrites it.
/// </summary>
public class HillPoint : MonoBehaviour
{
    [Range(0f, 1f)]
    [Tooltip("1 = smooth rounded hill. Lower flattens the top and steepens the sides; 0 is a " +
             "plateau with near-vertical cliffs.")]
    public float smoothness = 1f;

    [Range(0f, 1f)]
    [Tooltip("Rocky noise across this hill's shape. 0 = none.")]
    public float noise = 0f;
}
