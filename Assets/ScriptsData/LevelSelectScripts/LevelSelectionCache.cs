public static class LevelSelectionCache
{
    public static GridData SelectedGridData;

    // Boat state at the moment a LevelSelectEnter trigger fired.
    // Used to restore position if the player exits back through an entrance.
    public static string BoatSegmentID { get; set; }
    public static float  BoatProgress  { get; set; }
    public static bool   BoatIsLeftPath { get; set; }

    // Which ArenaEntrance index the boat entered through.
    // Read by LevelDataController to place the gameplay boat at the correct door.
    // -1 = not set (will fall back to entrance[0]).
    public static int SelectedEntranceIndex { get; set; } = -1;

    public static string JustExitedLevelID       { get; set; }
    public static int    JustExitedEntranceIndex { get; set; } = -1;

    public static string CurrentWorldScene { get; set; }
}
