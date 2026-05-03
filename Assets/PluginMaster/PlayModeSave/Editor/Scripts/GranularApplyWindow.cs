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

using System.Linq;
using UnityEngine;
namespace PluginMaster
{
    public class GranularApplyWindow : UnityEditor.EditorWindow
    {
        private Component _component;
        private UnityEditor.SerializedObject _serializedObject;
        private System.Collections.Generic.HashSet<string> _selectedProperties
            = new System.Collections.Generic.HashSet<string>();
        private System.Collections.Generic.HashSet<string> _allPropertyPaths
            = new System.Collections.Generic.HashSet<string>();
        private System.Collections.Generic.Dictionary<string, string> _propertyDisplayNames
            = new System.Collections.Generic.Dictionary<string, string>();

        public static void Show(Component component)
        {
            if (component == null)
            {
                Debug.LogError("Component is null. Cannot show Granular Apply Window.");
                return;
            }
            GranularApplyWindow window = GetWindow<GranularApplyWindow>(utility: true, "Select Changes to Apply");
            window._component = component;
            window._serializedObject = new UnityEditor.SerializedObject(component);
            if (window._serializedObject == null)
            {
                Debug.LogError("Failed to create SerializedObject. Ensure the component is valid.");
                return;
            }
            window.InitializePropertyHierarchy();
            window.Show();
        }
        private void InitializePropertyHierarchy()
        {
            _allPropertyPaths.Clear();
            _selectedProperties.Clear();
            _propertyDisplayNames.Clear();
            if (_serializedObject == null) return;
            UnityEditor.SerializedProperty iterator = _serializedObject.GetIterator();
            if (iterator == null) return;
            while (iterator.NextVisible(true))
            {
                if (iterator.name == "m_Script") continue;
                if (!iterator.propertyPath.Contains("."))
                {
                    string propertyPath = iterator.propertyPath;
                    _allPropertyPaths.Add(propertyPath);
                    _propertyDisplayNames[propertyPath] = iterator.displayName;
                    if (!_selectedProperties.Contains(propertyPath)) _selectedProperties.Add(propertyPath);
                }
            }
        }
        private void SelectAllProperties()
        {
            _selectedProperties.Clear();
            foreach (string propertyPath in _allPropertyPaths) _selectedProperties.Add(propertyPath);
        }
        private void DeselectAllProperties() => _selectedProperties.Clear();
        private Vector2 _scrollPosition;
        private void OnGUI()
        {
            if (_component == null || _serializedObject == null)
            {
                UnityEditor.EditorGUILayout.LabelField("Invalid Component.");
                return;
            }
            UnityEditor.EditorGUILayout.LabelField($"Apply Changes for: {_component.GetType().Name}",
                UnityEditor.EditorStyles.boldLabel);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select All", GUILayout.Width(100))) SelectAllProperties();
                if (GUILayout.Button("Deselect All", GUILayout.Width(100))) DeselectAllProperties();
            }

            _serializedObject.Update();

            using (var scrollScope = new UnityEditor.EditorGUILayout.ScrollViewScope(_scrollPosition,
                alwaysShowHorizontal: false, alwaysShowVertical: false, GUI.skin.horizontalScrollbar,
                GUI.skin.verticalScrollbar, background: GUIStyle.none))
            {
                _scrollPosition = scrollScope.scrollPosition;
                foreach (string propertyPath in _allPropertyPaths)
                {
                    string displayName = _propertyDisplayNames.ContainsKey(propertyPath)
                        ? _propertyDisplayNames[propertyPath] : propertyPath;
                    bool isSelected = _selectedProperties.Contains(propertyPath);
                    bool newSelection = UnityEditor.EditorGUILayout.ToggleLeft(displayName, isSelected);
                    if (newSelection && !isSelected) _selectedProperties.Add(propertyPath);
                    else if (!newSelection && isSelected) _selectedProperties.Remove(propertyPath);
                }
            }

            bool hasSelectedProperties = _selectedProperties.Count > 0;
            using (new UnityEditor.EditorGUI.DisabledScope(!hasSelectedProperties))
            {
                if (GUILayout.Button("Apply Selected Changes"))
                {
                    ApplySelectedProperties();
                    Close();
                }
            }
            if (!hasSelectedProperties)
                UnityEditor.EditorGUILayout.HelpBox("No properties selected."
                    + " Select at least one property to enable the Apply button.", UnityEditor.MessageType.Warning);
        }
        private void ApplySelectedProperties()
        {
            if (_selectedProperties.Count == 0)
            {
                Debug.LogWarning("No properties selected for applying changes.");
                return;
            }
            PlayModeSave.Apply(_component, _selectedProperties.ToArray());
        }
    }
}