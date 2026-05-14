using UnityEngine;

[ExecuteAlways]
public class SonarLatticeFollow : MonoBehaviour
{
    [SerializeField] Transform           boatTransform;
    [SerializeField] SonarPlaneGenerator generator;

    static readonly int GridOffsetID = Shader.PropertyToID("_GridOffset");

    MaterialPropertyBlock _hBlock;  // horizontal planes — full XZ offset
    MaterialPropertyBlock _vBlock;  // vertical planes   — X offset only

    void OnEnable()
    {
        _hBlock = new MaterialPropertyBlock();
        _vBlock = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (boatTransform == null || generator == null) return;

        // Follow boat in XZ — Y stays at wherever the lattice is placed
        Vector3 pos = transform.position;
        pos.x = boatTransform.position.x;
        pos.z = boatTransform.position.z;
        transform.position = pos;

        float scale = generator.GridWorldScale;
        float bx    = boatTransform.position.x * scale;
        float bz    = boatTransform.position.z * scale;

        _hBlock.SetVector(GridOffsetID, new Vector2(bx, bz));  // XZ — both axes
        _vBlock.SetVector(GridOffsetID, new Vector2(bx, 0f));  // X only — height stays fixed

        foreach (Renderer r in generator.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            bool isHorizontal = r.gameObject.name.StartsWith("Horizontal");
            r.SetPropertyBlock(isHorizontal ? _hBlock : _vBlock);
        }
    }
}
