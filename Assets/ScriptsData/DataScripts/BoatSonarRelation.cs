using UnityEngine;

public class BoatSonarRelation : MonoBehaviour
{
    [Header("Boat Tags")]
    [SerializeField] private string boatTag = "Boat";


    [Header("Materials")]
    public Material rockMaterial;
    

    [Header("Shader")]
    public string boatPositionProperty = "_BoatPosition";

    private Transform boat;
 

    void Update()
    {
        if (!boat)
        {
            GameObject b = GameObject.FindGameObjectWithTag(boatTag);
            if (b) boat = b.transform;
        }

      

        if (!boat || !rockMaterial )
            return;

        rockMaterial.SetVector(boatPositionProperty, boat.position);
        Shader.SetGlobalVector(boatPositionProperty, boat.position);

       
    }
}
