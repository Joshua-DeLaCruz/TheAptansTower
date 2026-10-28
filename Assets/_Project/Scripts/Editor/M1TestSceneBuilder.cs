using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the M1 player test scene from scratch, so the scene never has to be edited by hand.
/// Run it from Tools/Aptans/Build M1 Test Scene. Safe to run again: it rebuilds the scene.
/// </summary>
public static class M1TestSceneBuilder
{
    // --- Paths ---
    private const string ScenePath = "Assets/_Project/Scenes/M1_TestScene.unity";
    private const string PlaceholderSpritePath = "Assets/_Project/Art/Placeholder/Square.png";

    // --- Art settings (art is planned at 32 pixels per unit) ---
    private const int PixelsPerUnit = 32;
    private const int PlaceholderSize = 32;
    private const int ReferenceResolutionX = 640; // 640 / 32 = 20 units wide
    private const int ReferenceResolutionY = 360; // 360 / 32 = 11.25 units high

    // --- Layers ---
    private const string GroundLayerName = "Ground";
    private const string PlayerLayerName = "Player";

    // --- Placeholder colours (tweak here) ---
    private static readonly Color BackgroundColour = new Color(0.08f, 0.08f, 0.12f);
    private static readonly Color FloorColour = new Color(0.35f, 0.25f, 0.18f);
    private static readonly Color PlatformColour = new Color(0.45f, 0.45f, 0.5f);
    private static readonly Color HighPlatformColour = new Color(0.75f, 0.6f, 0.2f);
    private static readonly Color WallColour = new Color(0.25f, 0.25f, 0.3f);
    private static readonly Color PlayerColour = new Color(0.55f, 0.8f, 1f);

    // --- Sorting (no custom sorting layers yet, so order in layer is enough) ---
    private const int LevelSortingOrder = 0;
    private const int PlayerSortingOrder = 10;

    // --- Player starting physics values ---
    private static readonly Vector2 PlayerStartPosition = new Vector2(0f, -3.4f);
    private static readonly Vector2 PlayerColliderSize = new Vector2(0.8f, 1.8f);
    private const float PlayerGravityScale = 3f;

    [MenuItem("Tools/Aptans/Build M1 Test Scene")]
    public static void BuildScene()
    {
        // Ask before anything is changed, so Cancel really leaves the project untouched.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (File.Exists(ToFullPath(ScenePath)) &&
            !EditorUtility.DisplayDialog("Overwrite M1 Test Scene?",
                $"{ScenePath} already exists. Rebuild it? Any manual changes in that scene will be lost.",
                "Overwrite", "Cancel"))
            return;

        WarnIfNoRenderPipeline();

        Sprite square = GetOrCreatePlaceholderSprite();
        int groundLayer = EnsureLayer(GroundLayerName);
        int playerLayer = EnsureLayer(PlayerLayerName);
        // Writes TagManager to disk now; otherwise the new layers only exist in memory until the editor quits.
        AssetDatabase.SaveAssets();
        if (square == null || groundLayer < 0 || playerLayer < 0)
        {
            Debug.LogError("[M1TestSceneBuilder] Setup failed, scene not built. See the messages above.");
            return;
        }

        EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateCamera();
        CreateGlobalLight();
        CreateLevel(square, groundLayer);
        GameObject player = CreatePlayer(square, playerLayer);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            Debug.LogError($"[M1TestSceneBuilder] Could not save scene to {ScenePath}.");
            return;
        }

        SetAsOnlyBuildScene();

        Selection.activeGameObject = player;
        EditorGUIUtility.PingObject(player);
        Debug.Log($"[M1TestSceneBuilder] Built and saved {ScenePath}.");
    }

    // ---------------------------------------------------------------- Camera & light

    // The camera is static for now: the floor is 40 units wide but the view is about 20.
    // A camera follow is added in a later step.
    private static void CreateCamera()
    {
        var go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        go.transform.position = new Vector3(0f, 0f, -10f);

        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        // Half the visible height: 11.25 / 2. Pixel Perfect Camera recalculates this in Play mode anyway.
        cam.orthographicSize = ReferenceResolutionY / (float)PixelsPerUnit / 2f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = BackgroundColour;

        go.AddComponent<AudioListener>();

        // Snaps the camera to whole art pixels so 32 PPU sprites stay crisp.
        var pixelPerfect = go.AddComponent<PixelPerfectCamera>();
        pixelPerfect.assetsPPU = PixelsPerUnit;
        pixelPerfect.refResolutionX = ReferenceResolutionX;
        pixelPerfect.refResolutionY = ReferenceResolutionY;
    }

    private static void CreateGlobalLight()
    {
        // Under the 2D Renderer, Sprite-Lit sprites need a 2D light reaching their sorting layer;
        // a global light on all sorting layers makes sure there always is one.
        var go = new GameObject("Global Light 2D");
        var light = go.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.color = Color.white;
        light.intensity = 1f;

        int[] allSortingLayers = new int[SortingLayer.layers.Length];
        for (int i = 0; i < allSortingLayers.Length; i++)
            allSortingLayers[i] = SortingLayer.layers[i].id;
        light.targetSortingLayers = allSortingLayers;
    }

    // ---------------------------------------------------------------- Level

    // Floor top is at y = -4.5. Normal jump is planned at ~3 units.
    private static void CreateLevel(Sprite square, int groundLayer)
    {
        Transform level = new GameObject("Level").transform;

        CreateGroundPiece("Floor", level, new Vector2(0f, -5f), new Vector2(40f, 1f), FloorColour, square, groundLayer);

        // Top 2 units above the floor: easy normal jump.
        CreateGroundPiece("Platform_Low_A", level, new Vector2(-7f, -2.75f), new Vector2(4f, 0.5f), PlatformColour, square, groundLayer);
        // Top 2 units above Platform_Low_A: reachable by chaining jumps.
        CreateGroundPiece("Platform_Mid_B", level, new Vector2(-3f, -0.75f), new Vector2(3f, 0.5f), PlatformColour, square, groundLayer);
        // Top 2.5 units above the floor: near the edge of a normal jump.
        CreateGroundPiece("Platform_Low_C", level, new Vector2(3f, -2.25f), new Vector2(4f, 0.5f), PlatformColour, square, groundLayer);
        // Top 3.75 units above Platform_Low_C: needs a higher jump than normal (tests jump height tuning).
        CreateGroundPiece("Platform_High_D", level, new Vector2(8f, 1.5f), new Vector2(3f, 0.5f), HighPlatformColour, square, groundLayer);

        CreateGroundPiece("Wall_Left", level, new Vector2(-20.5f, 0.5f), new Vector2(1f, 12f), WallColour, square, groundLayer);
        CreateGroundPiece("Wall_Right", level, new Vector2(20.5f, 0.5f), new Vector2(1f, 12f), WallColour, square, groundLayer);
    }

    // Size lives on the collider and the tiled sprite, never on transform scale,
    // so physics code and child objects never have to deal with a scaled parent.
    private static GameObject CreateGroundPiece(string name, Transform parent, Vector2 position, Vector2 size,
        Color colour, Sprite square, int groundLayer)
    {
        var go = new GameObject(name);
        go.layer = groundLayer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;

        var box = go.AddComponent<BoxCollider2D>();
        box.size = size;

        CreateVisual(go.transform, square, colour, size, SpriteDrawMode.Tiled, LevelSortingOrder);
        return go;
    }

    // ---------------------------------------------------------------- Player

    private static GameObject CreatePlayer(Sprite square, int playerLayer)
    {
        var go = new GameObject("Player");
        go.layer = playerLayer;
        go.transform.position = PlayerStartPosition;

        var body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.gravityScale = PlayerGravityScale;

        var capsule = go.AddComponent<CapsuleCollider2D>();
        capsule.direction = CapsuleDirection2D.Vertical;
        capsule.size = PlayerColliderSize;

        // Sliced with no borders simply stretches the square to the size, keeping the Visual unscaled too.
        CreateVisual(go.transform, square, PlayerColour, PlayerColliderSize, SpriteDrawMode.Sliced, PlayerSortingOrder);

        AddPlayerComponents(go);
        return go;
    }

    // EXTENSION POINT for the next M1 steps. Add the player scripts here once they exist, for example:
    //   player.AddComponent<PlayerMovement>();
    //   player.AddComponent<PlayerDodge>();
    //   player.AddComponent<Health>();
    //   player.AddComponent<PlayerMeleeAttack>();
    //   player.AddComponent<PlayerDaggerThrow>();
    // Keep visuals on the Visual child; only logic and physics components go on this root object.
    private static void AddPlayerComponents(GameObject player)
    {
        // Edit-mode AddComponent runs Reset(), which fills in the default bindings.
        player.AddComponent<PlayerInputReader>();
    }

    // ---------------------------------------------------------------- Shared helpers

    // Visuals always live on their own child so real art can replace the placeholder without touching logic.
    private static SpriteRenderer CreateVisual(Transform parent, Sprite sprite, Color colour, Vector2 size,
        SpriteDrawMode drawMode, int sortingOrder)
    {
        var visual = new GameObject("Visual");
        visual.layer = parent.gameObject.layer;
        visual.transform.SetParent(parent, false);

        var renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = colour;
        renderer.drawMode = drawMode;
        renderer.size = size;
        renderer.sortingOrder = sortingOrder;
        if (drawMode == SpriteDrawMode.Tiled)
            renderer.tileMode = SpriteTileMode.Continuous;
        return renderer;
    }

    private static Sprite GetOrCreatePlaceholderSprite()
    {
        if (!File.Exists(ToFullPath(PlaceholderSpritePath)))
        {
            EnsureFolder(Path.GetDirectoryName(PlaceholderSpritePath).Replace('\\', '/'));

            var texture = new Texture2D(PlaceholderSize, PlaceholderSize, TextureFormat.RGBA32, false);
            var pixels = new Color32[PlaceholderSize * PlaceholderSize];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(ToFullPath(PlaceholderSpritePath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(PlaceholderSpritePath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[M1TestSceneBuilder] Created placeholder sprite at {PlaceholderSpritePath}.");
        }

        // Always checked, so a half-finished earlier run can't leave e.g. a 100 PPU sprite behind.
        if (!ConfigurePlaceholderImporter())
            return null;

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderSpritePath);
        if (sprite == null)
            Debug.LogError($"[M1TestSceneBuilder] {PlaceholderSpritePath} exists but is not imported as a Sprite. " +
                           "Delete it and run the builder again, or set Texture Type to Sprite.");
        return sprite;
    }

    /// <summary>Applies the placeholder import settings if they differ. Returns false if there is no TextureImporter.</summary>
    private static bool ConfigurePlaceholderImporter()
    {
        if (!(AssetImporter.GetAtPath(PlaceholderSpritePath) is TextureImporter importer))
        {
            Debug.LogError($"[M1TestSceneBuilder] No TextureImporter found for {PlaceholderSpritePath}. " +
                           "The PNG may have failed to import; delete it and run the builder again.");
            return false;
        }

        var current = new TextureImporterSettings();
        importer.ReadTextureSettings(current);
        bool alreadyCorrect =
            importer.textureType == TextureImporterType.Sprite &&
            importer.spriteImportMode == SpriteImportMode.Single &&
            Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnit) &&
            importer.filterMode == FilterMode.Point &&
            importer.textureCompression == TextureImporterCompression.Uncompressed &&
            current.spriteMeshType == SpriteMeshType.FullRect &&
            current.spriteAlignment == (int)SpriteAlignment.Center;
        if (alreadyCorrect)
            return true;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spritePivot = new Vector2(0.5f, 0.5f);
        // Full Rect is required for Tiled/Sliced draw modes to render correctly.
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);

        importer.SaveAndReimport();
        Debug.Log($"[M1TestSceneBuilder] Applied placeholder import settings to {PlaceholderSpritePath}.");
        return true;
    }

    /// <summary>Returns the layer index, adding the layer in the first free user slot if needed. -1 on failure.</summary>
    private static int EnsureLayer(string layerName)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogError("[M1TestSceneBuilder] Could not load ProjectSettings/TagManager.asset.");
            return -1;
        }

        var tagManager = new SerializedObject(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 0; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
            {
                Debug.Log($"[M1TestSceneBuilder] Layer '{layerName}' already exists at index {i}.");
                return i;
            }
        }

        // Slots 0-7 are reserved by Unity; user layers start at 8.
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[M1TestSceneBuilder] Added layer '{layerName}' at index {i}.");
                return i;
            }
        }

        Debug.LogError($"[M1TestSceneBuilder] No free user layer slot for '{layerName}'.");
        return -1;
    }

    private static void SetAsOnlyBuildScene()
    {
        // Replaces the old SampleScene entry, which points at a scene that no longer exists.
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log($"[M1TestSceneBuilder] Build Settings now contain only {ScenePath}.");
    }

    private static void WarnIfNoRenderPipeline()
    {
        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline == null)
        {
            Debug.LogWarning("[M1TestSceneBuilder] No URP asset is active (Graphics/Quality settings). " +
                             "The scene still builds, but Light 2D and Pixel Perfect Camera will have no effect.");
            return;
        }

        if (!(pipeline is UniversalRenderPipelineAsset urpAsset))
            return;

        // The default renderer index is internal in URP 17.4, so it is read through serialization instead.
        int defaultIndex = new SerializedObject(urpAsset).FindProperty("m_DefaultRendererIndex")?.intValue ?? 0;
        var rendererDataList = urpAsset.rendererDataList;
        ScriptableRendererData defaultRenderer =
            defaultIndex >= 0 && defaultIndex < rendererDataList.Length ? rendererDataList[defaultIndex] : null;

        if (!(defaultRenderer is Renderer2DData))
            Debug.LogWarning($"[M1TestSceneBuilder] The default renderer of '{urpAsset.name}' is not a 2D Renderer. " +
                             "Light 2D has no effect under the Universal Renderer.");
    }

    private static void EnsureFolder(string assetFolderPath)
    {
        if (AssetDatabase.IsValidFolder(assetFolderPath))
            return;

        string parent = Path.GetDirectoryName(assetFolderPath).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolderPath));
    }

    private static string ToFullPath(string assetPath)
    {
        return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
    }
}
