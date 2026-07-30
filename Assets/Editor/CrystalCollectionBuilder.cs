#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class CrystalCollectionBuilder
{
    const string ModelDir = "Assets/Models/CrystalPlanets";
    const string SpriteDir = "Assets/Sprites/CrystalPlanets";
    const string Prefab3DDir = "Assets/Prefabs/Crystal3D";
    const string MaterialDir = ModelDir + "/Materials";

    [MenuItem("Tools/Tap or Crash/Build Crystal Collection")]
    static void Build()
    {
        EnsureFolder("Assets/Prefabs", "Crystal3D");
        EnsureFolder(ModelDir, "Materials");

        Material crystalMaterial = BuildMaterial();
        Material spriteMaterial = BuildGameplaySpriteMaterial();
        GameObject[] gameplayPrefabs = new GameObject[10];

        for (int i = 1; i <= 10; i++)
        {
            string name = $"Crystal_{i:00}";
            ConfigureModelImporter(name);
            gameplayPrefabs[i - 1] = BuildSpritePrefab(name, spriteMaterial);
            Build3DPrefab(name, crystalMaterial);
        }

        AddOrUpdateLevel(gameplayPrefabs);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Crystal collection integration complete: 10 gameplay prefabs, 10 3D prefabs.");
    }

    static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    static void ConfigureModelImporter(string name)
    {
        string path = ModelDir + "/" + name + ".fbx";
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
            throw new InvalidOperationException("Missing Crystal model importer: " + path);

        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.meshCompression = ModelImporterMeshCompression.Medium;
        importer.optimizeMeshPolygons = true;
        importer.optimizeMeshVertices = true;
        importer.isReadable = false;
        importer.importAnimation = false;
        importer.SaveAndReimport();
    }

    static Material BuildMaterial()
    {
        string path = MaterialDir + "/Crystal_Palette_URP.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
                throw new InvalidOperationException("A compatible URP Lit shader was not found.");

            material = new Material(shader) { name = "Crystal_Palette_URP" };
            material.SetFloat("_Smoothness", 0.38f);
            material.SetFloat("_Metallic", 0.04f);
            AssetDatabase.CreateAsset(material, path);
        }

        Texture2D palette = AssetDatabase.LoadAssetAtPath<Texture2D>(
            ModelDir + "/Crystal_Palette.png");
        if (palette != null)
        {
            material.SetTexture("_BaseMap", palette);
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
        }
        return material;
    }

    static Material BuildGameplaySpriteMaterial()
    {
        string path = MaterialDir + "/Crystal_Gameplay_Presentation.mat";
        Shader shader = Shader.Find("TapOrCrash/Crystal Gameplay Presentation");
        if (shader == null)
            throw new InvalidOperationException("Crystal gameplay presentation shader was not found.");

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = "Crystal_Gameplay_Presentation" };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        material.SetFloat("_VisualScale", 4.4f);
        material.SetFloat("_GhostOpacity", 0.46f);
        material.SetFloat("_ShimmerStrength", 0.85f);
        material.SetColor("_ShimmerColor", new Color(0.55f, 0.95f, 1f, 1f));
        material.SetColor("_EnergyColor", new Color(0.68f, 0.38f, 1f, 1f));
        EditorUtility.SetDirty(material);
        return material;
    }

    static GameObject BuildSpritePrefab(string name, Material spriteMaterial)
    {
        string pngPath = SpriteDir + "/" + name + ".png";
        TextureImporter importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException("Missing Crystal sprite: " + pngPath);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.maxTextureSize = 1024;
        importer.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
        if (sprite == null)
            throw new InvalidOperationException("Crystal sprite import failed: " + pngPath);

        GameObject root = new GameObject(name);
        SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        if (spriteMaterial != null)
            renderer.sharedMaterial = spriteMaterial;
        root.AddComponent<CrystalPlanetAmbience>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
            root, SpriteDir + "/" + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static void Build3DPrefab(string name, Material material)
    {
        string modelPath = ModelDir + "/" + name + ".fbx";
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
            throw new InvalidOperationException("Missing Crystal model: " + modelPath);

        GameObject root = new GameObject(name + "_3D");
        GameObject instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
        if (instance == null)
            instance = UnityEngine.Object.Instantiate(model);

        instance.name = "Model";
        instance.transform.SetParent(root.transform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterial = material;

        PrefabUtility.SaveAsPrefabAsset(
            root, Prefab3DDir + "/" + name + "_3D.prefab");
        UnityEngine.Object.DestroyImmediate(root);
    }

    static void AddOrUpdateLevel(GameObject[] crystalPrefabs)
    {
        PlanetSpawner spawner = UnityEngine.Object.FindAnyObjectByType<PlanetSpawner>();
        if (spawner == null)
            throw new InvalidOperationException("PlanetSpawner was not found in the active scene.");

        List<PlanetSpawner.PlanetLevel> levels = spawner.levels != null
            ? new List<PlanetSpawner.PlanetLevel>(spawner.levels)
            : new List<PlanetSpawner.PlanetLevel>();

        PlanetSpawner.PlanetLevel crystal = null;
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] != null
                && string.Equals(levels[i].levelName, "Crystal", StringComparison.OrdinalIgnoreCase))
            {
                crystal = levels[i];
                break;
            }
        }

        if (crystal == null)
        {
            crystal = new PlanetSpawner.PlanetLevel();
            levels.Add(crystal);
        }

        crystal.levelName = "Crystal";
        crystal.prefabs = crystalPrefabs;
        spawner.levels = levels.ToArray();

        EditorUtility.SetDirty(spawner);
        EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
        EditorSceneManager.SaveScene(spawner.gameObject.scene);
    }
}
#endif
