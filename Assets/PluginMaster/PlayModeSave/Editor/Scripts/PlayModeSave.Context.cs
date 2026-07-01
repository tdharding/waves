/*
Copyright (c) Omar Duarte
Unauthorized copying of this file, via any medium is strictly prohibited.
Writen by Omar Duarte.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/
#pragma warning disable UDR0001

using UnityEngine;


namespace PluginMaster
{
    public partial class PlayModeSave : UnityEditor.EditorWindow
    {
        private const string TOOL_NAME = "Play Mode Save";
        [UnityEditor.MenuItem("CONTEXT/Component/Save Now", true, 1201)]
        private static bool ValidateSaveNowMenu(UnityEditor.MenuCommand command)
            => !UnityEditor.PrefabUtility.IsPartOfPrefabAsset(command.context) && Application.IsPlaying(command.context);
        [UnityEditor.MenuItem("CONTEXT/Component/Save Now", false, 1201)]
        private static void SaveNowMenu(UnityEditor.MenuCommand command)
            => Add(command.context as Component, SaveCommand.SAVE_NOW, false, true);

        [UnityEditor.MenuItem("CONTEXT/Component/Save When Exiting Play Mode", true, 1202)]
        private static bool ValidateSaveOnExtiMenu(UnityEditor.MenuCommand command) => ValidateSaveNowMenu(command);
        [UnityEditor.MenuItem("CONTEXT/Component/Save When Exiting Play Mode", false, 1202)]
        private static void SaveOnExitMenu(UnityEditor.MenuCommand command)
            => Add(command.context as Component, SaveCommand.SAVE_ON_EXITING_PLAY_MODE, false, true);

        [UnityEditor.MenuItem("CONTEXT/Component/Always Save When Exiting Play Mode", true, 1203)]
        private static bool ValidateAlwaysSaveOnExitMenu(UnityEditor.MenuCommand command)
           => (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene((command.context as Component).gameObject.scene)
                || UnityEditor.PrefabUtility.IsPartOfPrefabAsset(command.context)) ? false
            : !PMSData.Contains(new ComponentSaveDataKey(command.context as Component), out ComponentSaveDataKey foundKey);

        [UnityEditor.MenuItem("CONTEXT/Component/Always Save When Exiting Play Mode", false, 1203)]
        private static void AlwaysSaveOnExitMenu(UnityEditor.MenuCommand command)
            => Add(command.context as Component, SaveCommand.SAVE_ON_EXITING_PLAY_MODE, true, true);

        [UnityEditor.MenuItem("CONTEXT/Component/Remove From Save List", true, 1204)]
        private static bool ValidateRemoveFromSaveList(UnityEditor.MenuCommand command)
            => UnityEditor.PrefabUtility.IsPartOfPrefabAsset(command.context) ? false
            : PMSData.Contains(new ComponentSaveDataKey(command.context as Component), out ComponentSaveDataKey foundKey);
        [UnityEditor.MenuItem("CONTEXT/Component/Remove From Save List", false, 1204)]
        private static void RemoveFromSaveList(UnityEditor.MenuCommand command)
        {
            var component = command.context as Component;
            var key = new ComponentSaveDataKey(component);
            PMSData.Remove(key);
            CompDataRemoveKey(key);
        }

        [UnityEditor.MenuItem("CONTEXT/Component/Apply Play Mode Changes", true, 1210)]
        private static bool ValidateApplyMenu(UnityEditor.MenuCommand command)
        {
            var key = GetKey(command.context);
            return !Application.isPlaying && CompDataContainsKey(ref key);
        }
        [UnityEditor.MenuItem("CONTEXT/Component/Apply Play Mode Changes", false, 1210)]
        private static void ApplyMenu(UnityEditor.MenuCommand command)
        {
            var key = GetKey(command.context);
            Apply(key);
            if (!PMSData.Contains(key, out ComponentSaveDataKey foundKey)) CompDataRemoveKey(key);
        }
        [UnityEditor.MenuItem("CONTEXT/Component/Apply Play Mode Changes With Options...", true, 1211)]
        private static bool ValidateApplyWithGranularOptions(UnityEditor.MenuCommand command)
        {
            var key = GetKey(command.context);
            return !Application.isPlaying && CompDataContainsKey(ref key);
        }
        [UnityEditor.MenuItem("CONTEXT/Component/Apply Play Mode Changes With Options...", false, 1211)]
        private static void ApplyWithGranularOptions(UnityEditor.MenuCommand command)
        {
            Component component = command.context as Component;
            GranularApplyWindow.Show(component);
        }

        [UnityEditor.MenuItem("CONTEXT/Component/", false, 1300)]
        private static void Separator(UnityEditor.MenuCommand command) { }

        [UnityEditor.MenuItem("CONTEXT/ScriptableObject/Save Now", false, 1211)]
        private static void SaveScriptableObject(UnityEditor.MenuCommand command)
        {
            UnityEditor.AssetDatabase.Refresh();
            UnityEditor.EditorUtility.SetDirty(command.context);
            UnityEditor.AssetDatabase.SaveAssets();
        }
    }
}
#pragma warning restore UDR0001
