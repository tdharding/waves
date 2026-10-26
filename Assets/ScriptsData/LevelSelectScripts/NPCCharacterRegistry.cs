using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A level's characters, saved as one file: a name and the prefab that is its body. Several
/// characters can share one prefab. Made in the NPC Path Tool, where a path picks one of them to
/// walk it; a placed character's ObserverManNPCController holds the id of the one it is.
/// </summary>
public class NPCCharacterRegistry : ScriptableObject
{
    [System.Serializable]
    public class Character
    {
        [Tooltip("Never changes, so renaming a character keeps the one placed in the scene linked.")]
        public string id;
        public string name;
        [Tooltip("Its body — a prefab carrying an ObserverManNPCController.")]
        public GameObject prefab;
    }

    public List<Character> characters = new();

    public Character Find(string id)
        => string.IsNullOrEmpty(id) ? null : characters.Find(c => c != null && c.id == id);
}
