using UnityEngine;

public static class LevelSelectionCache
{
    public static GridData SelectedGridData;

    // Where the boat stood at the moment a LevelSelectEnter trigger fired.
    // Used to put it back if the player comes out the way they went in.
    public static bool    BoatHasPose { get; set; }
    public static Vector3 BoatPosition { get; set; }
    public static float   BoatHeading  { get; set; }

    // Which ArenaEntrance index the boat entered through.
    // Read by LevelDataController to place the gameplay boat at the correct door.
    // -1 = not set (will fall back to entrance[0]).
    public static int SelectedEntranceIndex { get; set; } = -1;

    public static string JustExitedLevelID       { get; set; }
    public static int    JustExitedEntranceIndex { get; set; } = -1;

    public static string CurrentWorldScene { get; set; }
}
