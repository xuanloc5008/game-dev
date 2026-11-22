using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AddCourtBoundaries
{
    private const string PrefabPath = "Assets/Prefabs/Map.prefab";

    static AddCourtBoundaries()
    {
        EditorApplication.delayCall += AddBoundaries;
    }

    [MenuItem("Tools/Map/Add Outer Boundary Colliders")]
    public static void AddBoundaries()
    {
        Debug.Log("=== [AddCourtBoundaries] Starting Outer Boundary Collider Setup ===");

        // 1. Update Map.prefab
        var mapRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (mapRoot == null)
        {
            Debug.LogError($"[AddCourtBoundaries] Could not load {PrefabPath}");
            return;
        }

        // Clean any existing barrier objects
        var existing = new List<GameObject>();
        for (int i = 0; i < mapRoot.transform.childCount; i++)
        {
            var child = mapRoot.transform.GetChild(i);
            if (child.name.StartsWith("CourtBarrier_"))
            {
                existing.Add(child.gameObject);
            }
        }
        foreach (var go in existing) Object.DestroyImmediate(go);

        // Exact measured bounds of the outer white lines:
        // X in [-13.10, +13.10], Z in [-10.10, +10.10], floor level at Y ≈ 0.15m
        float wallHeight = 4.0f;
        float centerY = 2.0f;
        float wallThickness = 0.25f;

        CreateBarrier(mapRoot, "CourtBarrier_West",  new Vector3(-13.10f, centerY, 0.0f),   new Vector3(wallThickness, wallHeight, 20.45f));
        CreateBarrier(mapRoot, "CourtBarrier_East",  new Vector3( 13.10f, centerY, 0.0f),   new Vector3(wallThickness, wallHeight, 20.45f));
        CreateBarrier(mapRoot, "CourtBarrier_North", new Vector3(0.0f,   centerY, -10.10f), new Vector3(26.45f, wallHeight, wallThickness));
        CreateBarrier(mapRoot, "CourtBarrier_South", new Vector3(0.0f,   centerY,  10.10f), new Vector3(26.45f, wallHeight, wallThickness));

        PrefabUtility.SaveAsPrefabAsset(mapRoot, PrefabPath);
        PrefabUtility.UnloadPrefabContents(mapRoot);
        Debug.Log($"[AddCourtBoundaries] Successfully saved 4 outer boundary colliders into {PrefabPath}.");

        // 2. Refresh active scene if one is currently open
        var activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.isLoaded)
        {
            var env = GameObject.Find("-- Environment --");
            if (env != null)
            {
                var sceneExisting = new List<GameObject>();
                for (int i = 0; i < env.transform.childCount; i++)
                {
                    var child = env.transform.GetChild(i);
                    if (child.name.StartsWith("CourtBarrier_"))
                    {
                        sceneExisting.Add(child.gameObject);
                    }
                }
                foreach (var go in sceneExisting) Object.DestroyImmediate(go);

                CreateBarrier(env, "CourtBarrier_West",  new Vector3(-13.10f, centerY, 0.0f),   new Vector3(wallThickness, wallHeight, 20.45f));
                CreateBarrier(env, "CourtBarrier_East",  new Vector3( 13.10f, centerY, 0.0f),   new Vector3(wallThickness, wallHeight, 20.45f));
                CreateBarrier(env, "CourtBarrier_North", new Vector3(0.0f,   centerY, -10.10f), new Vector3(26.45f, wallHeight, wallThickness));
                CreateBarrier(env, "CourtBarrier_South", new Vector3(0.0f,   centerY,  10.10f), new Vector3(26.45f, wallHeight, wallThickness));

                EditorSceneManager.MarkSceneDirty(activeScene);
                Debug.Log($"[AddCourtBoundaries] Refreshed boundary colliders in active scene '{activeScene.name}'.");
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log("=== [AddCourtBoundaries] Finished Successfully! ===");
    }

    private static void CreateBarrier(GameObject parent, string name, Vector3 localPos, Vector3 boxSize)
    {
        var barrier = new GameObject(name);
        barrier.layer = 0; // Layer Default for solid physics collision
        barrier.transform.SetParent(parent.transform, false);
        barrier.transform.localPosition = localPos;
        barrier.transform.localRotation = Quaternion.identity;
        barrier.transform.localScale = Vector3.one;

        var col = barrier.AddComponent<BoxCollider>();
        col.size = boxSize;
        col.center = Vector3.zero;
        col.isTrigger = false;
    }
}
