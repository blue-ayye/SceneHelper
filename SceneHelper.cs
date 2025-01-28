using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System;
using UnityEngine.SceneManagement;
using System.Reflection;

public static class SceneHelper
{
    private static string originalScenePath;
    private static List<string> expandedGameObjectPaths = new List<string>();
    private static string selectedGameObjectPath;

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            Scene currentScene = EditorSceneManager.GetActiveScene();
            if (currentScene.buildIndex != 0)
            {
                originalScenePath = currentScene.path;
                SaveState();

                // Load scene 0
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                EditorSceneManager.OpenScene(SceneUtility.GetScenePathByBuildIndex(0));
            }
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            if (!string.IsNullOrEmpty(originalScenePath))
            {
                EditorApplication.delayCall += () =>
                {
                    Scene currentScene = EditorSceneManager.GetActiveScene();
                    if (currentScene.path != originalScenePath)
                    {
                        EditorSceneManager.OpenScene(originalScenePath);
                        EditorApplication.delayCall += RestoreState;
                    }
                    else
                    {
                        RestoreState();
                    }
                    originalScenePath = null;
                };
            }
        }
    }

    private static void SaveState()
    {
        expandedGameObjectPaths.Clear();
        List<GameObject> expandedObjects = SceneHierarchyUtility.GetExpandedGameObjects();
        foreach (GameObject go in expandedObjects)
        {
            string path = GetGameObjectPath(go);
            expandedGameObjectPaths.Add(path);
        }

        GameObject selectedGameObject = Selection.activeGameObject;
        selectedGameObjectPath = selectedGameObject != null ? GetGameObjectPath(selectedGameObject) : "";

        // Save to EditorPrefs
        string json = JsonUtility.ToJson(new StringListWrapper(expandedGameObjectPaths));
        EditorPrefs.SetString("ExpandedGameObjectPaths", json);
        EditorPrefs.SetString("OriginalScenePath", originalScenePath);
        EditorPrefs.SetString("SelectedGameObjectPath", selectedGameObjectPath);
    }

    private static void RestoreState()
    {
        string json = EditorPrefs.GetString("ExpandedGameObjectPaths", "");
        if (!string.IsNullOrEmpty(json))
        {
            StringListWrapper wrapper = JsonUtility.FromJson<StringListWrapper>(json);
            expandedGameObjectPaths = wrapper.list;
        }

        // Collapse all first to avoid overlapping expansions
        List<GameObject> currentExpanded = SceneHierarchyUtility.GetExpandedGameObjects();
        foreach (GameObject go in currentExpanded)
        {
            SceneHierarchyUtility.SetExpanded(go, false);
        }

        // Restore each expanded GameObject
        foreach (string path in expandedGameObjectPaths)
        {
            GameObject go = FindGameObjectByPath(path);
            if (go != null)
            {
                SceneHierarchyUtility.SetExpanded(go, true);
            }
        }

        // Restore selected GameObject
        selectedGameObjectPath = EditorPrefs.GetString("SelectedGameObjectPath", "");
        if (!string.IsNullOrEmpty(selectedGameObjectPath))
        {
            GameObject selectedGameObject = FindGameObjectByPath(selectedGameObjectPath);
            if (selectedGameObject != null)
            {
                Selection.activeGameObject = selectedGameObject;
                // ping the selected GameObject
                EditorGUIUtility.PingObject(selectedGameObject);
            }
        }

        // Clear saved data
        EditorPrefs.DeleteKey("ExpandedGameObjectPaths");
        EditorPrefs.DeleteKey("OriginalScenePath");
        EditorPrefs.DeleteKey("SelectedGameObjectPath");
        expandedGameObjectPaths.Clear();
        selectedGameObjectPath = null;
    }

    private static string GetGameObjectPath(GameObject obj)
    {
        if (obj == null) return "";
        string path = obj.name;
        Transform parent = obj.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }

    private static GameObject FindGameObjectByPath(string path)
    {
        string[] names = path.Split('/');
        if (names.Length == 0) return null;

        GameObject current = null;
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == names[0])
            {
                current = root;
                break;
            }
        }

        if (current == null) return null;

        for (int i = 1; i < names.Length; i++)
        {
            bool found = false;
            foreach (Transform child in current.transform)
            {
                if (child.name == names[i])
                {
                    current = child.gameObject;
                    found = true;
                    break;
                }
            }
            if (!found) return null;
        }

        return current;
    }

    [Serializable]
    private class StringListWrapper
    {
        public List<string> list;

        public StringListWrapper(List<string> list)
        {
            this.list = list;
        }
    }
}

/// <summary>
/// Forked from https://github.com/sandolkakos/unity-utilities
/// Editor functionalities from internal SceneHierarchyWindow and SceneHierarchy classes.
/// For that we are using reflection.
/// </summary>
public static class SceneHierarchyUtility
{
    /// <summary>
    /// Check if the target GameObject is expanded (aka unfolded) in the Hierarchy view.
    /// </summary>
    public static bool IsExpanded(GameObject go)
    {
        return GetExpandedGameObjects().Contains(go);
    }

    /// <summary>
    /// Get a list of all GameObjects which are expanded (aka unfolded) in the Hierarchy view.
    /// </summary>
    public static List<GameObject> GetExpandedGameObjects()
    {
        object sceneHierarchy = GetSceneHierarchy();

        MethodInfo methodInfo = sceneHierarchy
            .GetType()
            .GetMethod("GetExpandedGameObjects");

        object result = methodInfo.Invoke(sceneHierarchy, new object[0]);

        return (List<GameObject>)result;
    }

    /// <summary>
    /// Set the target GameObject as expanded (aka unfolded) in the Hierarchy view.
    /// </summary>
    public static void SetExpanded(GameObject go, bool expand)
    {
        object sceneHierarchy = GetSceneHierarchy();

        MethodInfo methodInfo = sceneHierarchy
            .GetType()
            .GetMethod("ExpandTreeViewItem", BindingFlags.NonPublic | BindingFlags.Instance);

        methodInfo.Invoke(sceneHierarchy, new object[] { go.GetInstanceID(), expand });
    }

    /// <summary>
    /// Set the target GameObject and all children as expanded (aka unfolded) in the Hierarchy view.
    /// </summary>
    public static void SetExpandedRecursive(GameObject go, bool expand)
    {
        object sceneHierarchy = GetSceneHierarchy();

        MethodInfo methodInfo = sceneHierarchy
            .GetType()
            .GetMethod("SetExpandedRecursive", BindingFlags.Public | BindingFlags.Instance);

        methodInfo.Invoke(sceneHierarchy, new object[] { go.GetInstanceID(), expand });
    }

    private static object GetSceneHierarchy()
    {
        EditorWindow window = GetHierarchyWindow();

        object sceneHierarchy = typeof(EditorWindow).Assembly
            .GetType("UnityEditor.SceneHierarchyWindow")
            .GetProperty("sceneHierarchy")
            .GetValue(window);

        return sceneHierarchy;
    }

    private static EditorWindow GetHierarchyWindow()
    {
        // For it to open, so that it the current focused window.
        EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
        return EditorWindow.focusedWindow;
    }
}