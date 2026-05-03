using UnityEngine;
using System.Collections.Generic;

public class SonarGridController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject gridPrefab;
    [SerializeField] private Transform gridParent;



    [Header("Grid Settings")]
    [SerializeField] private float tileSize = 0.838f;
    [SerializeField] private float verticalOffset = -5f;
    [SerializeField] private int tilesX = 3;
    [SerializeField] private int tilesZ = 3;


    private Transform boat;
    private readonly List<Transform> tiles = new List<Transform>();
    private Vector3Int currentCell;
    private Vector3 sonarOrigin;

    private bool initialized = false;
   [SerializeField] private Material gridMaterial;
     [SerializeField] private Material soulFishMaterial;
    private static readonly int SonarOriginID = Shader.PropertyToID("_SoulStartPosition");

    void Update()
    {
        if (!initialized)
        {
            TryInitialize();
            return;
        }

        Vector3Int newCell = WorldToCell(boat.position);
        if (newCell != currentCell)
        {
            currentCell = newCell;
            SnapTiles();
        }

        if (gridMaterial != null)
        {
            gridMaterial.SetVector(SonarOriginID, boat.position);
            soulFishMaterial.SetVector(SonarOriginID, boat.position);
        }

    }

    /// <summary>
    /// Set the SoulBoat transform directly. Called from LevelDataController after SpawnSoulBoat().
    /// </summary>
    public void SetSoulBoat(Transform soulBoat)
    {
        boat = soulBoat;
    }

    void TryInitialize()
    {
        if (!boat)
            return; // wait until SetSoulBoat() is called

        sonarOrigin = boat.position;
        currentCell = WorldToCell(boat.position);



        int halfX = tilesX / 2;
        int halfZ = tilesZ / 2;

        tiles.Clear();



        for (int x = 0; x < tilesX; x++)
        {
            for (int z = 0; z < tilesZ; z++)
            {
                Transform t = Instantiate(gridPrefab, gridParent).transform;
                tiles.Add(t);
            }
        }





        if (gridMaterial != null)
        {
            gridMaterial.SetVector(SonarOriginID, sonarOrigin);
            soulFishMaterial.SetVector(SonarOriginID, sonarOrigin);
        }



        SnapTiles();
        initialized = true;
    }

    void SnapTiles()
    {
        int i = 0;

        int offsetX = tilesX / 2;
        int offsetZ = tilesZ / 2;

        for (int x = 0; x < tilesX; x++)
        {
            for (int z = 0; z < tilesZ; z++)
            {
                int centeredX = x - offsetX;
                int centeredZ = z - offsetZ;

                Vector3Int cell = currentCell + new Vector3Int(centeredX, 0, centeredZ);
                if (i < tiles.Count)
                {
                    tiles[i].position = CellToWorld(cell);
                }
                i++;

            }
        }

    }

    Vector3Int WorldToCell(Vector3 world)
    {
        Vector3 local = world - sonarOrigin;

        return new Vector3Int(
            Mathf.RoundToInt(local.x / tileSize),
            0,
            Mathf.RoundToInt(local.z / tileSize)
        );
    }

    Vector3 CellToWorld(Vector3Int cell)
    {
        return sonarOrigin + new Vector3(
            cell.x * tileSize,
            verticalOffset,
            cell.z * tileSize
        );
    }
}
