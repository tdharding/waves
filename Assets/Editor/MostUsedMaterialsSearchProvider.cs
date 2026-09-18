using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Search;
using UnityEditor.SearchService;
using UnityEngine;
using UnityEngine.Search;

// "Most Used" tab in the material picker. Lists every .mat in the project that something actually
// references, most referenced first. A reference is a scene, prefab or .asset file that points at
// the material directly — a prefab placed fifty times in a scene still counts once, for the prefab.
//
// The tab lives in the picker registered below (see Preferences > Search > Object Selector).
// Materials that live inside imported models aren't .mat files, so they never show up here.
static class MostUsedMaterialsSearchProvider
{
    const string ProviderId   = "most_used_materials";
    const string ProviderName = "Most Used";

    static Dictionary<string, int> useCounts;

    [SearchItemProvider]
    static SearchProvider CreateProvider()
    {
        return new SearchProvider(ProviderId, ProviderName)
        {
            filterId          = "used:",
            priority          = 10,
            showDetails       = true,
            showDetailsOptions = ShowDetailsOptions.Inspector | ShowDetailsOptions.Actions,
            fetchItems        = (context, items, provider) => FetchItems(context, provider),
            fetchThumbnail    = (item, context) => Thumbnail(item.id),
            fetchPreview      = (item, context, size, options) => Thumbnail(item.id),
            toObject          = (item, type) => AssetDatabase.LoadAssetAtPath(item.id, type ?? typeof(Material)),
            trackSelection    = (item, context) => EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(item.id)),
        };
    }

    static IEnumerable<SearchItem> FetchItems(SearchContext context, SearchProvider provider)
    {
        // No type check here: in a non-material slot, toObject loads nothing and the picker drops the item.
        if (useCounts == null)
            Rescan();

        // Plain words in the search box narrow by name; tokens like t:Material are the picker's own.
        string[] words = context.searchQuery
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.Contains(':'))
            .ToArray();

        foreach (var entry in useCounts.OrderByDescending(e => e.Value).ThenBy(e => e.Key))
        {
            string name = Path.GetFileNameWithoutExtension(entry.Key);
            if (words.Any(w => name.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0))
                continue;

            string description = entry.Value == 1 ? "1 use" : entry.Value + " uses";
            // Lower score sorts first.
            yield return provider.CreateItem(context, entry.Key, -entry.Value, name, description, null, null);
        }
    }

    static Texture2D Thumbnail(string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
            return null;
        // The preview renders in the background, so the first ask can come back empty.
        return AssetPreview.GetAssetPreview(material) ?? AssetDatabase.GetCachedIcon(path) as Texture2D;
    }

    [MenuItem("Tools/Most Used Materials/Rescan")]
    static void Rescan()
    {
        useCounts = new Dictionary<string, int>();

        foreach (string path in AssetDatabase.GetAllAssetPaths())
        {
            if (!IsReferencer(path))
                continue;

            foreach (string dependency in AssetDatabase.GetDependencies(path, false))
            {
                if (!dependency.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    continue;
                useCounts.TryGetValue(dependency, out int count);
                useCounts[dependency] = count + 1;
            }
        }
    }

    static bool IsReferencer(string path)
    {
        return path.StartsWith("Assets/")
            && (path.EndsWith(".unity") || path.EndsWith(".prefab") || path.EndsWith(".asset"));
    }

    // Unity's own Advanced picker only ever asks its built-in providers, so a custom tab needs a picker
    // of its own. This one opens the same lists Unity's does, plus Most Used, and only for material slots —
    // anything else falls through to Unity's picker.
    const string SelectorId = "most_used_materials_selector";

    static ISearchView pickerWindow;

    [AdvancedObjectSelectorValidator(SelectorId)]
    static bool CanOpenPicker(ObjectSelectorSearchContext context)
    {
        return context.requiredTypes != null && context.requiredTypes.Any(t => t == typeof(Material));
    }

    [AdvancedObjectSelector(SelectorId, "Materials with Most Used", 100)]
    static void HandlePickerEvent(AdvancedObjectSelectorEventType eventType, in AdvancedObjectSelectorParameters parameters)
    {
        switch (eventType)
        {
            case AdvancedObjectSelectorEventType.OpenAndSearch:
                OpenPicker(parameters);
                break;
            case AdvancedObjectSelectorEventType.SetSearchFilter:
                pickerWindow?.SetSearchText(parameters.searchFilter);
                break;
            case AdvancedObjectSelectorEventType.EndSession:
                if ((parameters.context.endSessionModes & ObjectSelectorSearchEndSessionModes.CloseSelector) != 0)
                    pickerWindow?.Close();
                pickerWindow = null;
                break;
        }
    }

    static void OpenPicker(in AdvancedObjectSelectorParameters parameters)
    {
        var context = parameters.context;
        var providerIds = new List<string> { ProviderId, "adb", "asset" };
        if (context.visibleObjects != VisibleObjects.Assets)
            providerIds.Add("scene");

        var searchContext = SearchService.CreateContext(providerIds, "t:Material", SearchFlags.OpenPicker);
        var state = SearchViewState.CreatePickerState("Material", searchContext,
            parameters.selectorClosedHandler, parameters.trackingHandler,
            nameof(Material), typeof(Material), SearchViewFlags.None);
        pickerWindow = SearchService.ShowPicker(state);
    }

    // Any scene, prefab or .asset saved, moved or deleted drops the counts; the next picker opening rescans.
    class Invalidator : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (useCounts == null)
                return;
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsReferencer))
                useCounts = null;
        }
    }
}
