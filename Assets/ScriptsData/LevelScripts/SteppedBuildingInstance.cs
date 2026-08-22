using UnityEngine;

// Rides on a spawned stepped building and remembers how it was built (preset + size + seed).
// In the editor during play it watches its source preset and rebuilds the mesh whenever the
// preset is edited, so tuning a preset in the inspector updates every building using it live —
// no stop/replay. Preset edits are asset changes, so they persist after you exit play mode.
// The watch loop is editor-only; in a build the mesh is built once on spawn and left alone.
[DisallowMultipleComponent]
public class SteppedBuildingInstance : MonoBehaviour
{
    ProceduralCubeBuilding _builder;
    SteppedBuildingPreset  _preset;
    float _width, _length, _top, _depth;
    int   _seed;
    SteppedBuildingConfig _applied;   // snapshot of the last config actually built

    public void Init(ProceduralCubeBuilding builder, SteppedBuildingPreset preset,
                     float width, float length, float top, float depth, int seed)
    {
        _builder = builder; _preset = preset;
        _width = width; _length = length; _top = top; _depth = depth; _seed = seed;
        Rebuild();
    }

    void Rebuild()
    {
        if (_builder == null || _preset == null) return;
        _builder.BuildStepped(_width, _length, _top, _depth, _preset.config, _seed);
        _applied = _preset.config.Copy();
    }

#if UNITY_EDITOR
    void Update()
    {
        if (_preset == null || _applied == null) return;
        if (!_applied.ValueEquals(_preset.config)) Rebuild();
    }
#endif
}
