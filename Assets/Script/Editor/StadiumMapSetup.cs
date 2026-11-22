using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StadiumMapSetup
{
    private const string FbxPath = "Assets/Maps/Stadium_Blender_V1.fbx";
    private const string MaterialsDir = "Assets/Maps/Materials";
    private const string PrefabDir = "Assets/Prefabs";
    private const string PrefabPath = "Assets/Prefabs/Map.prefab";
    private const string MultiplayerScenePath = "Assets/Scenes/Multiplayer.unity";
    private const string SingleplayerScenePath = "Assets/Scenes/Singleplayer.unity";
    private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

    [MenuItem("Tools/Map/Inspect Stadium FBX")]
    public static void InspectStadium()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[InspectStadium] Could not load ModelImporter at {FbxPath}");
            return;
        }

        Debug.Log($"[InspectStadium] ModelImporter: globalScale={importer.globalScale}, useFileScale={importer.useFileScale}, fileScale={importer.fileScale}, importCameras={importer.importCameras}, importLights={importer.importLights}, addCollider={importer.addCollider}");

        var fbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbxPrefab == null)
        {
            Debug.LogError($"[InspectStadium] Could not load GameObject at {FbxPath}");
            return;
        }

        var tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxPrefab, tempScene);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        Bounds totalBounds = new Bounds();
        bool hasBounds = false;
        foreach (var mr in renderers)
        {
            if (hasBounds) totalBounds.Encapsulate(mr.bounds);
            else { totalBounds = mr.bounds; hasBounds = true; }
        }

        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        string playerInfo = "N/A";
        if (playerPrefab != null)
        {
            var pInst = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, tempScene);
            var pRenderers = pInst.GetComponentsInChildren<Renderer>(true);
            Bounds pBounds = new Bounds();
            bool pHas = false;
            foreach (var pr in pRenderers)
            {
                if (pHas) pBounds.Encapsulate(pr.bounds);
                else { pBounds = pr.bounds; pHas = true; }
            }
            playerInfo = $"center={pBounds.center}, size={pBounds.size}";
        }

        Debug.Log($"[InspectStadium] Renderers={renderers.Length}, TotalWorldBounds: center={totalBounds.center}, size={totalBounds.size}, min={totalBounds.min}, max={totalBounds.max} | PlayerBounds: {playerInfo}");
    }

    [MenuItem("Tools/Map/Setup Stadium Map")]
    public static void SetupStadiumMap()
    {
        Debug.Log("=== Starting Stadium Map Setup ===");

        if (!Directory.Exists(PrefabDir)) Directory.CreateDirectory(PrefabDir);
        if (!Directory.Exists(MaterialsDir)) Directory.CreateDirectory(MaterialsDir);

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        // 1. Configure FBX ModelImporter
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[SetupStadiumMap] Could not find FBX at {FbxPath}!");
            return;
        }

        bool needsReimport = false;
        if (importer.importCameras) { importer.importCameras = false; needsReimport = true; }
        if (importer.importLights) { importer.importLights = false; needsReimport = true; }
        if (!importer.addCollider) { importer.addCollider = true; needsReimport = true; }
        if (importer.materialLocation != ModelImporterMaterialLocation.InPrefab)
        {
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            needsReimport = true;
        }

        if (needsReimport)
        {
            importer.SaveAndReimport();
        }

        // Extract embedded materials so they live cleanly as editable URP materials in Assets/Maps/Materials
        ExtractMaterialsFromFbx(FbxPath, MaterialsDir);
        AssetDatabase.SaveAssets();
        importer.SaveAndReimport();
        AssetDatabase.Refresh();

        // 2. Load FBX asset and build clean Map.prefab in an empty temp scene
        var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbxAsset == null)
        {
            Debug.LogError($"[SetupStadiumMap] Failed to load FBX GameObject at {FbxPath}!");
            return;
        }

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer == -1) groundLayer = 7;

        var tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var mapRoot = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset, tempScene);
        PrefabUtility.UnpackPrefabInstance(mapRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        mapRoot.name = "-- Environment --";
        mapRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        mapRoot.transform.localScale = Vector3.one;

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
                if (mc == null)
                {
                    mc = t.gameObject.AddComponent<MeshCollider>();
                }
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                colliderCount++;
            }
        }

        Debug.Log($"[SetupStadiumMap] Prepared Stadium root with {allTransforms.Length} nodes, {meshCount} meshes, {colliderCount} MeshColliders on Layer {groundLayer} (Ground).");

        // Save Map.prefab
        bool prefabSuccess;
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(mapRoot, PrefabPath, out prefabSuccess);
        if (!prefabSuccess || savedPrefab == null)
        {
            Debug.LogError($"[SetupStadiumMap] Failed to save prefab at {PrefabPath}!");
            return;
        }
        Debug.Log($"[SetupStadiumMap] Saved {PrefabPath} successfully.");

        // 3. Update Multiplayer.unity
        var mpScene = EditorSceneManager.OpenScene(MultiplayerScenePath, OpenSceneMode.Single);
        if (!mpScene.IsValid())
        {
            Debug.LogError($"[SetupStadiumMap] Failed to open {MultiplayerScenePath}!");
            return;
        }

        var oldMpEnv = GameObject.Find("-- Environment --");
        if (oldMpEnv != null)
        {
            Object.DestroyImmediate(oldMpEnv);
        }

        var mpMap = (GameObject)PrefabUtility.InstantiatePrefab(savedPrefab, mpScene);
        mpMap.name = "-- Environment --";
        mpMap.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        mpMap.transform.localScale = Vector3.one;

        // Place 4 spawn points symmetrically on the stadium court floor facing center
        Physics.SyncTransforms();
        int groundMask = 1 << groundLayer;
        Vector3[] spawnTargets = new Vector3[]
        {
            new Vector3(-4.0f, 0f, -2.5f),
            new Vector3(-4.0f, 0f,  2.5f),
            new Vector3( 4.0f, 0f, -2.5f),
            new Vector3( 4.0f, 0f,  2.5f)
        };

        var spawnParent = GameObject.Find("Spawn Positions");
        if (spawnParent != null)
        {
            spawnParent.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            spawnParent.transform.localScale = Vector3.one;

            for (int i = 0; i < spawnParent.transform.childCount && i < spawnTargets.Length; i++)
            {
                var sp = spawnParent.transform.GetChild(i);
                Vector3 targetXZ = spawnTargets[i];
                float floorY = 0.05f;
                if (Physics.Raycast(new Vector3(targetXZ.x, 10f, targetXZ.z), Vector3.down, out RaycastHit hit, 25f, groundMask))
                {
                    floorY = hit.point.y + 0.05f;
                }
                sp.position = new Vector3(targetXZ.x, floorY, targetXZ.z);
                Vector3 lookDir = new Vector3(-targetXZ.x, 0f, -targetXZ.z);
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    sp.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
                }
            }
        }

        // Move Item pickup onto the court floor if present so it doesn't fall into the void
        var itemGo = GameObject.Find("Item");
        if (itemGo != null)
        {
            float itemFloorY = 1.0f;
            if (Physics.Raycast(new Vector3(0f, 10f, 0f), Vector3.down, out RaycastHit itemHit, 25f, groundMask))
            {
                itemFloorY = itemHit.point.y + 1.0f;
            }
            itemGo.transform.position = new Vector3(0f, itemFloorY, 0f);
        }

        PositionLobbyCameras();

        EditorSceneManager.MarkSceneDirty(mpScene);
        EditorSceneManager.SaveScene(mpScene);
        Debug.Log($"[SetupStadiumMap] Saved {MultiplayerScenePath} with Stadium Map.prefab and updated Spawn Positions.");

        // 4. Update Singleplayer.unity
        var spScene = EditorSceneManager.OpenScene(SingleplayerScenePath, OpenSceneMode.Single);
        if (!spScene.IsValid())
        {
            Debug.LogError($"[SetupStadiumMap] Failed to open {SingleplayerScenePath}!");
            return;
        }

        var oldSpEnv = GameObject.Find("-- Environment --");
        if (oldSpEnv != null)
        {
            Object.DestroyImmediate(oldSpEnv);
        }

        var spMap = (GameObject)PrefabUtility.InstantiatePrefab(savedPrefab, spScene);
        spMap.name = "-- Environment --";
        spMap.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        spMap.transform.localScale = Vector3.one;

        Physics.SyncTransforms();
        var spPlayer = GameObject.Find("Singleplayer Controller");
        if (spPlayer != null)
        {
            float spFloorY = 1.0f;
            if (Physics.Raycast(new Vector3(0f, 10f, 0f), Vector3.down, out RaycastHit spHit, 25f, groundMask))
            {
                spFloorY = spHit.point.y + 1.1f;
            }
            spPlayer.transform.position = new Vector3(0f, spFloorY, 0f);
            spPlayer.transform.rotation = Quaternion.identity;
        }

        PositionLobbyCameras();

        EditorSceneManager.MarkSceneDirty(spScene);
        EditorSceneManager.SaveScene(spScene);
        Debug.Log($"[SetupStadiumMap] Saved {SingleplayerScenePath} with Stadium Map.prefab.");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("=== Stadium Map Setup Completed Successfully ===");
    }

    [MenuItem("Tools/Map/Verify Stadium Map")]
    public static void VerifyStadiumMap()
    {
        Debug.Log("=== Verifying Stadium Map ===");
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer == -1) groundLayer = 7;
        int groundMask = 1 << groundLayer;

        foreach (var scenePath in new[] { MultiplayerScenePath, SingleplayerScenePath })
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var env = GameObject.Find("-- Environment --");
            if (env == null)
            {
                Debug.LogError($"[Verify] {scenePath}: '-- Environment --' NOT FOUND!");
                continue;
            }

            int totalNodes = 0;
            int validMeshes = 0;
            int validColliders = 0;
            int nonGround = 0;
            int missingMat = 0;

            foreach (var t in env.GetComponentsInChildren<Transform>(true))
            {
                totalNodes++;
                if (t.gameObject.layer != groundLayer) nonGround++;

                var mf = t.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0)
                    validMeshes++;

                var mc = t.GetComponent<MeshCollider>();
                if (mc != null && mc.sharedMesh != null && mc.sharedMesh.vertexCount > 0)
                    validColliders++;

                var mr = t.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    foreach (var mat in mr.sharedMaterials)
                    {
                        if (mat == null || mat.shader == null || mat.shader.name.Contains("Error"))
                            missingMat++;
                    }
                }
            }

            Debug.Log($"[Verify] {scenePath}: Nodes={totalNodes}, ValidMeshes={validMeshes}, ValidColliders={validColliders}, NonGround={nonGround}, Missing/ErrorMaterials={missingMat}");

            Physics.SyncTransforms();
            if (scenePath == MultiplayerScenePath)
            {
                var spawnParent = GameObject.Find("Spawn Positions");
                if (spawnParent != null)
                {
                    for (int i = 0; i < spawnParent.transform.childCount; i++)
                    {
                        var sp = spawnParent.transform.GetChild(i);
                        if (Physics.Raycast(sp.position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, groundMask))
                            Debug.Log($"[Verify] Spawn '{sp.name}' at {sp.position} hits '{hit.collider.name}' (Layer {hit.collider.gameObject.layer}) at y={hit.point.y:F3}");
                        else
                            Debug.LogError($"[Verify] Spawn '{sp.name}' at {sp.position} DID NOT HIT Ground!");
                    }
                }
            }
            else if (scenePath == SingleplayerScenePath)
            {
                var player = GameObject.Find("Singleplayer Controller");
                if (player != null)
                {
                    if (Physics.Raycast(player.transform.position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, groundMask))
                        Debug.Log($"[Verify] Singleplayer Controller at {player.transform.position} hits '{hit.collider.name}' (Layer {hit.collider.gameObject.layer}) at y={hit.point.y:F3}");
                    else
                        Debug.LogError($"[Verify] Singleplayer Controller at {player.transform.position} DID NOT HIT Ground!");
                }
            }
        }

        // Verify isolated Prefab instantiation
        var emptyScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[Verify] Could not load {PrefabPath}!");
            return;
        }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, emptyScene);
        int instMeshes = 0, instColliders = 0;
        foreach (var t in inst.GetComponentsInChildren<Transform>(true))
        {
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0) instMeshes++;
            var mc = t.GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh != null && mc.sharedMesh.vertexCount > 0) instColliders++;
        }
        Debug.Log($"[Verify] Fresh Map.prefab instance: ValidMeshes={instMeshes}, ValidColliders={instColliders}");
        Debug.Log("=== Verification Completed ===");
    }

    private static void PositionLobbyCameras()
    {
        Vector3 camPos = new Vector3(-9.5f, 6.0f, -9.5f);
        Quaternion camRot = Quaternion.LookRotation((new Vector3(0f, 0.8f, 0f) - camPos).normalized, Vector3.up);

        var menuCam = GameObject.Find("MenuCam");
        if (menuCam != null)
        {
            menuCam.transform.SetPositionAndRotation(camPos, camRot);
        }

        var mainCam = GameObject.Find("Main Camera");
        if (mainCam != null && mainCam.transform.parent == null)
        {
            mainCam.transform.SetPositionAndRotation(camPos, camRot);
        }
    }

    private static void ExtractMaterialsFromFbx(string fbxPath, string destinationFolder)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        var urpLitShader = Shader.Find("Universal Render Pipeline/Lit");

        var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        foreach (var asset in assets)
        {
            if (asset is Material mat)
            {
                string targetPath = $"{destinationFolder}/{mat.name}.mat";
                Material extractedMat = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
                if (extractedMat == null)
                {
                    string err = AssetDatabase.ExtractAsset(mat, targetPath);
                    if (!string.IsNullOrEmpty(err))
                    {
                        Debug.LogWarning($"[SetupStadiumMap] Material extract warning for {mat.name}: {err}");
                    }
                    AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceSynchronousImport);
                    extractedMat = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
                }

                if (extractedMat != null)
                {
                    if (urpLitShader != null && (extractedMat.shader == null || extractedMat.shader.name != "Universal Render Pipeline/Lit"))
                    {
                        extractedMat.shader = urpLitShader;
                    }

                    if (mat.name.Contains("LED"))
                    {
                        extractedMat.EnableKeyword("_EMISSION");
                        extractedMat.SetColor("_EmissionColor", new Color(1.0f, 0.2f, 0.05f) * 2.5f);
                    }

                    if (importer != null)
                    {
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), mat.name), extractedMat);
                    }
                    EditorUtility.SetDirty(extractedMat);
                }
            }
        }
    }
}
