#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class MechanicalCollectionBuilder
{
    const string ModelDir = "Assets/Models/MechanicalPlanets";
    const string SpriteDir = "Assets/Sprites/MechanicalPlanets";
    const string Prefab3DDir = "Assets/Prefabs/Mechanical3D";
    const string MaterialDir = ModelDir + "/Materials";

    [MenuItem("Tools/Tap or Crash/Build Mechanical Collection")]
    static void Build()
    {
        EnsureFolder("Assets/Prefabs", "Mechanical3D");
        EnsureFolder(ModelDir, "Materials");

        Material mechanicalMaterial = BuildMaterial();
        GameObject[] gameplayPrefabs = new GameObject[10];

        for (int i = 1; i <= 10; i++)
        {
            string name = $"Mechanical_{i:00}";
            ConfigureModelImporter(name);
            gameplayPrefabs[i - 1] = BuildSpritePrefab(name);
            Build3DPrefab(name, mechanicalMaterial);
        }

        AddOrUpdateLevel(gameplayPrefabs);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Mechanical collection integration complete: 10 gameplay prefabs, 10 3D prefabs.");
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
            throw new InvalidOperationException("Missing Mechanical model importer: " + path);

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
        string path = MaterialDir + "/Mechanical_Palette_URP.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
                throw new InvalidOperationException("A compatible URP Lit shader was not found.");

            material = new Material(shader) { name = "Mechanical_Palette_URP" };
            AssetDatabase.CreateAsset(material, path);
        }

        // The one reflective pack: machined metal with tight highlights.
        material.SetFloat("_Smoothness", 0.62f);
        material.SetFloat("_Metallic", 0.80f);

        Texture2D palette = AssetDatabase.LoadAssetAtPath<Texture2D>(
            ModelDir + "/Mechanical_Palette.png");
        if (palette != null)
        {
            material.SetTexture("_BaseMap", palette);
            material.SetColor("_BaseColor", Color.white);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static GameObject BuildSpritePrefab(string name)
    {
        string pngPath = SpriteDir + "/" + name + ".png";
        TextureImporter importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException("Missing Mechanical sprite: " + pngPath);

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
            throw new InvalidOperationException("Mechanical sprite import failed: " + pngPath);

        GameObject root = new GameObject(name);
        SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        root.AddComponent<MechanicalPlanetAmbience>();

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
            throw new InvalidOperationException("Missing Mechanical model: " + modelPath);

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

        PrefabUtility.SaveAsPrefabAsset(root, Prefab3DDir + "/" + name + "_3D.prefab");
        UnityEngine.Object.DestroyImmediate(root);
    }

    static void AddOrUpdateLevel(GameObject[] mechanicalPrefabs)
    {
        PlanetSpawner spawner = UnityEngine.Object.FindAnyObjectByType<PlanetSpawner>();
        if (spawner == null)
            throw new InvalidOperationException("PlanetSpawner was not found in the active scene.");

        List<PlanetSpawner.PlanetLevel> levels = spawner.levels != null
            ? new List<PlanetSpawner.PlanetLevel>(spawner.levels)
            : new List<PlanetSpawner.PlanetLevel>();

        PlanetSpawner.PlanetLevel mechanical = null;
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] != null
                && string.Equals(levels[i].levelName, "Mechanical", StringComparison.OrdinalIgnoreCase))
            {
                mechanical = levels[i];
                break;
            }
        }

        if (mechanical == null)
        {
            mechanical = new PlanetSpawner.PlanetLevel();
            levels.Add(mechanical);
        }

        mechanical.levelName = "Mechanical";
        mechanical.prefabs = mechanicalPrefabs;
        spawner.levels = levels.ToArray();

        EditorUtility.SetDirty(spawner);
        EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
        EditorSceneManager.SaveScene(spawner.gameObject.scene);
    }
}
#endif
