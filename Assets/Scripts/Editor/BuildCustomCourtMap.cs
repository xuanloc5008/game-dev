using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildCustomCourtMap
{
    private const string MaterialsDir = "Assets/Maps/Materials";
    private const string PrefabPath = "Assets/Prefabs/Map.prefab";
    private const string MultiplayerScenePath = "Assets/Scenes/Multiplayer.unity";
    private const string SingleplayerScenePath = "Assets/Scenes/Singleplayer.unity";

    [MenuItem("Tools/Map/Build Custom Minimalist Court")]
    public static void BuildCourt()
    {
        Debug.Log("=== Starting Custom Minimalist Court Build ===");

        if (!Directory.Exists(MaterialsDir)) Directory.CreateDirectory(MaterialsDir);

        // 1. Create or load materials (URP Lit)
        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) urpLit = Shader.Find("Standard");

        var matCourtFloor = GetOrCreateMaterial("Mat_CourtFloor", new Color(0.91f, 0.92f, 0.94f), 0.7f, 0.0f, urpLit);
        var matEndZone = GetOrCreateMaterial("Mat_EndZone", new Color(0.82f, 0.84f, 0.87f), 0.7f, 0.0f, urpLit);
        var matBlackLine = GetOrCreateMaterial("Mat_CourtLineBlack", new Color(0.08f, 0.08f, 0.09f), 0.8f, 0.0f, urpLit);
        var matWall = GetOrCreateMaterial("Mat_PerimeterWall", new Color(0.20f, 0.21f, 0.23f), 0.6f, 0.0f, urpLit);

        AssetDatabase.SaveAssets();

        // 2. Build Court Hierarchy in a temporary scene
        var tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var mapRoot = new GameObject("Map");
        mapRoot.transform.position = Vector3.zero;
        mapRoot.transform.rotation = Quaternion.identity;
        mapRoot.transform.localScale = Vector3.one;

        // Base foundation
        CreateBox(mapRoot, "Foundation_Base", new Vector3(0f, -0.25f, 0f), new Vector3(16.6f, 0.3f, 24.6f), matWall, 7);

        // Floors
        // Total Court Width = 16m (X: -8 to +8), Total Length = 24m (Z: -12 to +12)
        // South End Zone: Z in [-12, -9.5], length 2.5m, center Z = -10.75m
        // North End Zone: Z in [9.5, 12], length 2.5m, center Z = +10.75m
        // South Main Zone: Z in [-9.5, 0], length 9.5m, center Z = -4.75m
        // North Main Zone: Z in [0, 9.5], length 9.5m, center Z = +4.75m
        CreateBox(mapRoot, "Floor_EndZoneSouth", new Vector3(0f, -0.05f, -10.75f), new Vector3(15.6f, 0.1f, 2.3f), matEndZone, 7);
        CreateBox(mapRoot, "Floor_EndZoneNorth", new Vector3(0f, -0.05f, 10.75f), new Vector3(15.6f, 0.1f, 2.3f), matEndZone, 7);
        CreateBox(mapRoot, "Floor_MainSouth", new Vector3(0f, -0.05f, -4.75f), new Vector3(15.6f, 0.1f, 9.1f), matCourtFloor, 7);
        CreateBox(mapRoot, "Floor_MainNorth", new Vector3(0f, -0.05f, 4.75f), new Vector3(15.6f, 0.1f, 9.1f), matCourtFloor, 7);

        // Black Court Lines (Slightly raised Y to avoid Z-fighting, thickness 0.2m)
        float lineY = 0.005f; // sits 5mm above the floor
        float lineThickness = 0.015f;
        // Center line
        CreateBox(mapRoot, "Line_Center", new Vector3(0f, lineY, 0f), new Vector3(15.6f, lineThickness, 0.2f), matBlackLine, 7);
        // End zone lines
        CreateBox(mapRoot, "Line_EndSouth", new Vector3(0f, lineY, -9.5f), new Vector3(15.6f, lineThickness, 0.2f), matBlackLine, 7);
        CreateBox(mapRoot, "Line_EndNorth", new Vector3(0f, lineY, 9.5f), new Vector3(15.6f, lineThickness, 0.2f), matBlackLine, 7);
        // Outer border lines
        CreateBox(mapRoot, "Line_BorderWest", new Vector3(-7.9f, lineY, 0f), new Vector3(0.2f, lineThickness, 24.0f), matBlackLine, 7);
        CreateBox(mapRoot, "Line_BorderEast", new Vector3(7.9f, lineY, 0f), new Vector3(0.2f, lineThickness, 24.0f), matBlackLine, 7);
        CreateBox(mapRoot, "Line_BorderSouth", new Vector3(0f, lineY, -11.9f), new Vector3(16.0f, lineThickness, 0.2f), matBlackLine, 7);
        CreateBox(mapRoot, "Line_BorderNorth", new Vector3(0f, lineY, 11.9f), new Vector3(16.0f, lineThickness, 0.2f), matBlackLine, 7);

        // Low Perimeter Walls (0.8m high so ragdolls don't fall off)
        float wallHeight = 0.8f;
        float wallY = wallHeight * 0.5f;
        float wallThickness = 0.3f;
        CreateBox(mapRoot, "Wall_West", new Vector3(-8.15f, wallY, 0f), new Vector3(wallThickness, wallHeight, 24.6f), matWall, 7);
        CreateBox(mapRoot, "Wall_East", new Vector3(8.15f, wallY, 0f), new Vector3(wallThickness, wallHeight, 24.6f), matWall, 7);
        CreateBox(mapRoot, "Wall_South", new Vector3(0f, wallY, -12.15f), new Vector3(16.6f, wallHeight, wallThickness), matWall, 7);
        CreateBox(mapRoot, "Wall_North", new Vector3(0f, wallY, 12.15f), new Vector3(16.6f, wallHeight, wallThickness), matWall, 7);

        // Out-of-bounds safety catch plane
        var oob = CreateBox(mapRoot, "OutOfBounds_Catch", new Vector3(0f, -5f, 0f), new Vector3(60f, 1f, 60f), matWall, 7);
        var oobCol = oob.GetComponent<BoxCollider>();
        if (oobCol != null) oobCol.isTrigger = false; // solid ground below

        // 3. Save as Prefab
        if (!Directory.Exists("Assets/Prefabs")) Directory.CreateDirectory("Assets/Prefabs");
        PrefabUtility.SaveAsPrefabAsset(mapRoot, PrefabPath);
        Debug.Log($"[BuildCourt] Saved new Map.prefab with {mapRoot.transform.childCount} parts.");

        // 4. Update Multiplayer scene
        UpdateMultiplayerScene();

        // 5. Update Singleplayer scene
        UpdateSingleplayerScene();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("=== Custom Minimalist Court Build Completed Successfully! ===");
    }

    private static GameObject CreateBox(GameObject parent, string name, Vector3 localPos, Vector3 scale, Material mat, int layer)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = layer;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = mat;

        var col = go.GetComponent<BoxCollider>();
        if (col != null) col.enabled = true;

        return go;
    }

    private static Material GetOrCreateMaterial(string name, Color color, float smoothness, float metallic, Shader shader)
    {
        string path = $"{MaterialsDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.color = color;
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void UpdateMultiplayerScene()
    {
        var scene = EditorSceneManager.OpenScene(MultiplayerScenePath, OpenSceneMode.Single);

        // Place or replace Map instance
        var existingEnv = GameObject.Find("-- Environment --");
        if (existingEnv != null) Object.DestroyImmediate(existingEnv);
        var oldMap = GameObject.Find("Map");
        if (oldMap != null) Object.DestroyImmediate(oldMap);

        var mapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var mapInstance = (GameObject)PrefabUtility.InstantiatePrefab(mapPrefab, scene);
        mapInstance.name = "-- Environment --";
        mapInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // Set spawn positions for 2 teams facing each other across the center line:
        // Team 1 (South End Zone):
        //   Player 1 Spawn: (-3.5, 0.25, -10.75), facing North (rot Y=0)
        //   Player 2 Spawn: (+3.5, 0.25, -10.75), facing North (rot Y=0)
        // Team 2 (North End Zone):
        //   Player 3 Spawn: (-3.5, 0.25, +10.75), facing South (rot Y=180)
        //   Player 4 Spawn: (+3.5, 0.25, +10.75), facing South (rot Y=180)
        SetSpawnTransform("Player 1 Spawn", new Vector3(-3.5f, 0.25f, -10.75f), Quaternion.Euler(0f, 0f, 0f));
        SetSpawnTransform("Player 2 Spawn", new Vector3(3.5f, 0.25f, -10.75f), Quaternion.Euler(0f, 0f, 0f));
        SetSpawnTransform("Player 3 Spawn", new Vector3(-3.5f, 0.25f, 10.75f), Quaternion.Euler(0f, 180f, 0f));
        SetSpawnTransform("Player 4 Spawn", new Vector3(3.5f, 0.25f, 10.75f), Quaternion.Euler(0f, 180f, 0f));

        // Position camera nicely
        var menuCam = GameObject.Find("MenuCam");
        if (menuCam != null)
        {
            menuCam.transform.SetPositionAndRotation(new Vector3(-14f, 11f, -16f), Quaternion.Euler(26f, 41f, 0f));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[BuildCourt] Multiplayer scene updated with new court & spawn points.");
    }

    private static void UpdateSingleplayerScene()
    {
        var scene = EditorSceneManager.OpenScene(SingleplayerScenePath, OpenSceneMode.Single);

        var existingEnv = GameObject.Find("-- Environment --");
        if (existingEnv != null) Object.DestroyImmediate(existingEnv);
        var oldMap = GameObject.Find("Map");
        if (oldMap != null) Object.DestroyImmediate(oldMap);

        var mapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var mapInstance = (GameObject)PrefabUtility.InstantiatePrefab(mapPrefab, scene);
        mapInstance.name = "-- Environment --";
        mapInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // Adjust Singleplayer Player position onto the new court
        var playerInst = GameObject.Find("Player");
        if (playerInst != null)
        {
            playerInst.transform.SetPositionAndRotation(new Vector3(0f, 0.25f, -5f), Quaternion.Euler(0f, 0f, 0f));
        }

        var menuCam = GameObject.Find("MenuCam");
        if (menuCam != null)
        {
            menuCam.transform.SetPositionAndRotation(new Vector3(-14f, 11f, -16f), Quaternion.Euler(26f, 41f, 0f));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[BuildCourt] Singleplayer scene updated with new court.");
    }

    private static void SetSpawnTransform(string name, Vector3 worldPos, Quaternion worldRot)
    {
        var go = GameObject.Find(name);
        if (go == null) return;

        go.layer = 0; // Layer Default, not Ground
        go.transform.position = worldPos;
        go.transform.rotation = worldRot;

        // Position child "Position 1..4" right at zero local offset so its world position equals worldPos
        for (int i = 0; i < go.transform.childCount; i++)
        {
            var child = go.transform.GetChild(i);
            child.gameObject.layer = 0;
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            var childCols = child.GetComponentsInChildren<Collider>(true);
            foreach (var c in childCols) Object.DestroyImmediate(c);
        }

        // Clean any renderers and colliders
        var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
        foreach (var r in renderers) Object.DestroyImmediate(r);

        var filters = go.GetComponentsInChildren<MeshFilter>(true);
        foreach (var f in filters) Object.DestroyImmediate(f);

        var colliders = go.GetComponentsInChildren<Collider>(true);
        foreach (var c in colliders) Object.DestroyImmediate(c);
    }
}
