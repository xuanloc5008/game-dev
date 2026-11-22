using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupStadiumScaled
{
    private const string FbxPath = "Assets/Maps/Stadium_Blender_V1.fbx";
    private const string MaterialsDir = "Assets/Maps/Materials";
    private const string PrefabPath = "Assets/Prefabs/Map.prefab";
    private const string MultiplayerScenePath = "Assets/Scenes/Multiplayer.unity";
    private const string SingleplayerScenePath = "Assets/Scenes/Singleplayer.unity";
    private const float ScaleMultiplier = 2.0f; // Scale map 2x bigger

    [MenuItem("Tools/Map/Setup Scaled Stadium")]
    public static void Execute()
    {
        Debug.Log($"=== Setting up Stadium Map with Scale Multiplier = {ScaleMultiplier}x ===");

        // 1. Configure FBX ModelImporter with 2x globalScale
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[SetupStadiumScaled] Could not find ModelImporter at {FbxPath}");
            return;
        }

        importer.globalScale = ScaleMultiplier;
        importer.importCameras = false;
        importer.importLights = false;
        importer.addCollider = true;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.SaveAndReimport();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        // Remap materials to Assets/Maps/Materials/*.mat with URP Lit
        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        var assets = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
        foreach (var asset in assets)
        {
            if (asset is Material mat)
            {
                string matPath = $"{MaterialsDir}/{mat.name}.mat";
                var diskMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (diskMat != null)
                {
                    if (urpLit != null) diskMat.shader = urpLit;
                    if (mat.name.Contains("LED"))
                    {
                        diskMat.EnableKeyword("_EMISSION");
                        diskMat.SetColor("_EmissionColor", new Color(1.0f, 0.2f, 0.05f) * 2.5f);
                    }
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), mat.name), diskMat);
                    EditorUtility.SetDirty(diskMat);
                }
            }
        }
        importer.SaveAndReimport();
        AssetDatabase.Refresh();

        // 2. Load FBX asset and build clean Map.prefab
        var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbxAsset == null)
        {
            Debug.LogError($"[SetupStadiumScaled] Failed to load FBX GameObject at {FbxPath}!");
            return;
        }

        var tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var mapRoot = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset, tempScene);
        PrefabUtility.UnpackPrefabInstance(mapRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        mapRoot.name = "-- Environment --";
        mapRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        mapRoot.transform.localScale = Vector3.one;

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer == -1) groundLayer = 7;

        int meshCount = 0;
        int colliderCount = 0;
        var allTransforms = mapRoot.GetComponentsInChildren<Transform>(true);
        foreach (var t in allTransforms)
        {
            t.gameObject.layer = groundLayer;
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0)
            {
                meshCount++;
                var mc = t.GetComponent<MeshCollider>();
                if (mc == null) mc = t.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                colliderCount++;
            }
        }

        Debug.Log($"[SetupStadiumScaled] Prepared Stadium root with {allTransforms.Length} nodes, {meshCount} meshes, {colliderCount} MeshColliders on Layer {groundLayer}.");

        // Save Map.prefab
        PrefabUtility.SaveAsPrefabAsset(mapRoot, PrefabPath);
        Debug.Log($"[SetupStadiumScaled] Saved {PrefabPath} successfully.");

        // 3. Update Multiplayer.unity
        UpdateMultiplayerScene();

        // 4. Update Singleplayer.unity
        UpdateSingleplayerScene();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("=== Scaled Stadium Setup Completed Successfully! ===");
    }

    private static void UpdateMultiplayerScene()
    {
        var scene = EditorSceneManager.OpenScene(MultiplayerScenePath, OpenSceneMode.Single);

        var oldEnv = GameObject.Find("-- Environment --");
        if (oldEnv != null) Object.DestroyImmediate(oldEnv);
        var oldMap = GameObject.Find("Map");
        if (oldMap != null) Object.DestroyImmediate(oldMap);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "-- Environment --";
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        // At 2x scale, court floor is at y ≈ 0.154m. Place spawn points at y = 0.40m
        // Court corners at 2x are at (-8, -5), (-8, 5), (8, -5), (8, 5)
        SetSpawn("Player 1 Spawn", new Vector3(-8.0f, 0.40f, -5.0f), Quaternion.Euler(0f, 45f, 0f));
        SetSpawn("Player 2 Spawn", new Vector3(-8.0f, 0.40f, 5.0f), Quaternion.Euler(0f, 135f, 0f));
        SetSpawn("Player 3 Spawn", new Vector3(8.0f, 0.40f, -5.0f), Quaternion.Euler(0f, -45f, 0f));
        SetSpawn("Player 4 Spawn", new Vector3(8.0f, 0.40f, 5.0f), Quaternion.Euler(0f, -135f, 0f));

        // Position camera to fit the 2x scaled stadium
        var menuCam = GameObject.Find("MenuCam");
        if (menuCam != null)
        {
            menuCam.transform.SetPositionAndRotation(new Vector3(-19.0f, 12.0f, -19.0f), Quaternion.Euler(24f, 45f, 0f));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SetupStadiumScaled] Multiplayer.unity updated.");
    }

    private static void UpdateSingleplayerScene()
    {
        var scene = EditorSceneManager.OpenScene(SingleplayerScenePath, OpenSceneMode.Single);

        var oldEnv = GameObject.Find("-- Environment --");
        if (oldEnv != null) Object.DestroyImmediate(oldEnv);
        var oldMap = GameObject.Find("Map");
        if (oldMap != null) Object.DestroyImmediate(oldMap);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "-- Environment --";
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        var playerInst = GameObject.Find("Player");
        if (playerInst != null)
        {
            playerInst.transform.SetPositionAndRotation(new Vector3(0f, 0.40f, 0f), Quaternion.identity);
        }

        var menuCam = GameObject.Find("MenuCam");
        if (menuCam != null)
        {
            menuCam.transform.SetPositionAndRotation(new Vector3(-19.0f, 12.0f, -19.0f), Quaternion.Euler(24f, 45f, 0f));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SetupStadiumScaled] Singleplayer.unity updated.");
    }

    private static void SetSpawn(string name, Vector3 pos, Quaternion rot)
    {
        var go = GameObject.Find(name);
        if (go == null) return;

        go.layer = 0;
        go.transform.position = pos;
        go.transform.rotation = rot;

        for (int i = 0; i < go.transform.childCount; i++)
        {
            var child = go.transform.GetChild(i);
            child.gameObject.layer = 0;
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            foreach (var c in child.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        }

        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) Object.DestroyImmediate(mr);
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(mf);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
    }
}
