using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MenuPresentationBaker
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string PresentationPrefabPath = "Assets/Resources/Menu/MainMenuPresentation.prefab";
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Tap or Crash/Author Serialized Main Menu")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Main Menu authoring is Edit Mode only.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError("Open " + ScenePath + " before authoring the Main Menu.");
            return;
        }

        GameObject presentation = GameObject.Find("MainMenuPresentation");
        if (presentation == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PresentationPrefabPath);
            if (prefab == null)
            {
                Debug.LogError("Missing authoritative presentation prefab: " + PresentationPrefabPath);
                return;
            }
            presentation = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            presentation.name = "MainMenuPresentation";
        }

        Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
        CoinManager coin = UnityEngine.Object.FindFirstObjectByType<CoinManager>();
        ShipSkinManager shop = UnityEngine.Object.FindFirstObjectByType<ShipSkinManager>();
        if (canvas == null || coin == null || shop == null)
        {
            Debug.LogError("Main Menu authoring requires the scene Canvas, CoinManager and ShipSkinManager.");
            return;
        }

        Transform start = canvas.transform.Find("StartPanel");
        Transform gameUi = canvas.transform.Find("GameUI");
        if (start == null || gameUi == null)
        {
            Debug.LogError("Main Menu authoring requires Canvas/StartPanel and Canvas/GameUI.");
            return;
        }

        Invoke(coin, "CreateCoinCounter");
        Invoke(shop, "CreateShopButton");
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        RocketFuelGaugeView.Create(start, canvasRect);
        RocketFuelGaugeView.Create(gameUi, canvasRect);
        RocketFuelPopup.Create(canvas.transform);
        if (canvas.GetComponent<RocketFuelHud>() == null) canvas.gameObject.AddComponent<RocketFuelHud>();
        if (canvas.GetComponent<SafeAreaFitter>() == null) canvas.gameObject.AddComponent<SafeAreaFitter>();

        GameObject polishHost = new GameObject("__MenuAuthoringPolish");
        try
        {
            VisualPolishController polish = polishHost.AddComponent<VisualPolishController>();
            typeof(VisualPolishController).GetField("canvas", PrivateInstance).SetValue(polish, canvas);
            string[] methods = { "ConfigureCanvas", "AdoptIconFamily", "StyleStartScreen", "StyleHud", "StyleGameOver", "StylePause", "StyleCommonButtons" };
            foreach (string method in methods) Invoke(polish, method);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(polishHost);
        }

        RepairMovedPresentationComponents(scene);

        RepairPresentationPrefab();
        ApplySerializedMenuPreview(canvas, start, gameUi);

        Transform legacyCta = start.Find("TAP TO START");
        if (legacyCta != null) legacyCta.name = "TapToLaunch";
        RocketFuelPopup popup = canvas.transform.Find("RocketFuelPopup")?.GetComponent<RocketFuelPopup>();
        if (popup != null) popup.gameObject.SetActive(false);

        Camera camera = Camera.main;
        if (camera != null)
        {
            camera.transform.position = new Vector3(-0.9719825f, 8.401645f, -10f);
            camera.transform.rotation = Quaternion.identity;
            camera.orthographicSize = 6.275694f;
        }
        RocketController rocket = UnityEngine.Object.FindFirstObjectByType<RocketController>();
        if (rocket != null)
        {
            rocket.transform.position = new Vector3(1.3834295f, 8.785839f, 0f);
            rocket.transform.rotation = Quaternion.Euler(0f, 0f, 114.17728f);
            rocket.transform.localScale = new Vector3(0.40101036f, 0.40101036f, 1.0025259f);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("Failed to save the authoritative Main Menu scene: " + ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        if (scene.isDirty)
            throw new InvalidOperationException("The authoritative Main Menu scene remained dirty after saving.");
        Debug.Log("Serialized Main Menu authored successfully. Existing objects were adopted; no player data was written.");
    }

    [MenuItem("Tools/Tap or Crash/Apply Main Menu Visual Preview")]
    public static void ApplyVisualPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Main Menu visual preview authoring is Edit Mode only.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError("Open " + ScenePath + " before applying the Main Menu visual preview.");
            return;
        }

        Canvas canvas = UIRootCanvas.Resolve();
        Transform start = canvas != null ? canvas.transform.Find("StartPanel") : null;
        Transform gameUi = canvas != null ? canvas.transform.Find("GameUI") : null;
        if (canvas == null || start == null || gameUi == null)
            throw new InvalidOperationException("The serialized Canvas, StartPanel or GameUI is missing.");

        ApplySerializedMenuPreview(canvas, start, gameUi);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("Failed to save the Main Menu visual preview: " + ScenePath);
        AssetDatabase.SaveAssets();
        if (scene.isDirty)
            throw new InvalidOperationException("The scene remained dirty after saving the Main Menu visual preview.");

        Debug.Log("Main Menu visual preview applied. No hierarchy or player data was changed.");
    }

    static void RepairPresentationPrefab()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(PresentationPrefabPath);
        try
        {
            contents.SetActive(true);
            contents.hideFlags = HideFlags.None;
            string[] required =
            {
                "Layer1_Nebulae", "Layer2_FarStars", "Layer3_NearStars", "Layer4_BrightStars",
                "Layer5_Asteroids", "Layer6_AtmosphericGlow", "HeroAura", "Vignette",
                "MenuLavaHero", "MenuLavaAtmosphere",
                "MenuLavaHeatHalo", "BrandEmblem", "BrandEmblemHalo", "BrandEmblemSpark",
                "MenuRocketEngineGlow"
            };
            foreach (string name in required)
            {
                Transform child = FindDeep(contents.transform, name);
                if (child == null)
                    throw new InvalidOperationException("Authoritative Main Menu prefab is missing required object: " + name);
                child.gameObject.SetActive(true);
                child.gameObject.hideFlags = HideFlags.None;
                Renderer renderer = child.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = true;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, PresentationPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    static void ApplySerializedMenuPreview(Canvas canvas, Transform start, Transform gameUi)
    {
        start.gameObject.SetActive(true);
        Image dim = start.GetComponent<Image>();
        if (dim == null) throw new InvalidOperationException("Canvas/StartPanel requires its serialized dim Image.");
        dim.enabled = true;
        dim.color = new Color(0.02f, 0.035f, 0.09f, 0.22f);

        string[] requiredStartChildren =
        {
            "LaunchGlow", "LaunchPlate", "TapToLaunch", "BestScorePlate", "BestScoreText",
            "ControlHint", "RocketFuelHud", "SoundButton", "HelpButton", "DayNightButton", "ShopButton"
        };
        foreach (string name in requiredStartChildren)
        {
            Transform child = start.Find(name);
            if (child == null) throw new InvalidOperationException("Canvas/StartPanel is missing required object: " + name);
            child.gameObject.SetActive(true);
            SetGraphicsEnabled(child);
        }

        Transform coin = canvas.transform.Find("CoinCounter");
        if (coin == null) throw new InvalidOperationException("Canvas is missing the serialized CoinCounter.");
        coin.gameObject.SetActive(true);
        SetGraphicsEnabled(coin);

        Transform popup = canvas.transform.Find("RocketFuelPopup");
        if (popup == null) throw new InvalidOperationException("Canvas is missing the serialized RocketFuelPopup.");
        CanvasGroup popupGroup = popup.GetComponent<CanvasGroup>();
        if (popupGroup != null)
        {
            popupGroup.alpha = 0f;
            popupGroup.interactable = false;
            popupGroup.blocksRaycasts = false;
        }
        popup.gameObject.SetActive(false);

        gameUi.gameObject.SetActive(false);
        SetActive(canvas.transform, "ScorePlate", false);
        SetActive(canvas.transform, "ScoreText", false);
        SetActive(canvas.transform, "AsteroidWarningArrow", false);

        ApplyStaticTints(start);
        ApplyStaticTints(coin);
        ApplyStaticTints(popup);

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        ApplyFuelPreview(start, canvasRect);
        ApplyFuelPreview(gameUi, canvasRect);
    }

    static void ApplyStaticTints(Transform root)
    {
        EnsureThemeRegistryForAuthoring();
        UIDesign.Palette palette = UIDesign.ApprovedStartupPalette;
        foreach (UITinted tint in root.GetComponentsInChildren<UITinted>(true))
            tint.ApplyPalette(palette);
    }

    // Theme accents register through RuntimeInitializeOnLoadMethod in a player.
    // Invoke those same pure registration methods in Edit Mode so authoring never
    // falls through to UIDesign's cyan safety fallback.
    static void EnsureThemeRegistryForAuthoring()
    {
        foreach (Type type in TypeCache.GetTypesDerivedFrom<PlanetAmbience>())
        {
            MethodInfo register = type.GetMethod("RegisterTheme",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (register != null && register.GetParameters().Length == 0)
                register.Invoke(null, null);
        }
    }

    static void ApplyFuelPreview(Transform parent, RectTransform canvasRect)
    {
        Transform root = parent.Find(RocketFuelGaugeView.RootName);
        RocketFuelGaugeView gauge = root != null ? root.GetComponent<RocketFuelGaugeView>() : null;
        if (gauge == null || !gauge.ApplyAuthoringPreview(canvasRect))
            throw new InvalidOperationException(parent.name + " has an incomplete serialized Fuel gauge.");
    }

    static void SetActive(Transform parent, string name, bool active)
    {
        Transform child = parent.Find(name);
        if (child != null) child.gameObject.SetActive(active);
    }

    static void SetGraphicsEnabled(Transform root)
    {
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.enabled = true;
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    static void Invoke(object target, string method)
    {
        MethodInfo info = target.GetType().GetMethod(method, PrivateInstance);
        if (info == null) throw new MissingMethodException(target.GetType().Name, method);
        info.Invoke(target, null);
    }

    static void RepairMovedPresentationComponents(Scene scene)
    {
        foreach (UITinted old in UnityEngine.Object.FindObjectsByType<UITinted>(FindObjectsInactive.Include))
        {
            if (old.gameObject.scene != scene || HasScriptAsset(old)) continue;
            SerializedObject serialized = new SerializedObject(old);
            int role = serialized.FindProperty("role").enumValueIndex;
            float alpha = serialized.FindProperty("alphaScale").floatValue;
            GameObject host = old.gameObject;
            UnityEngine.Object.DestroyImmediate(old);
            UITinted replacement = host.AddComponent<UITinted>();
            SerializedObject target = new SerializedObject(replacement);
            target.FindProperty("role").enumValueIndex = role;
            target.FindProperty("alphaScale").floatValue = alpha;
            target.ApplyModifiedPropertiesWithoutUndo();
        }

        foreach (UIShadowLink old in UnityEngine.Object.FindObjectsByType<UIShadowLink>(FindObjectsInactive.Include))
        {
            if (old.gameObject.scene != scene || HasScriptAsset(old)) continue;
            SerializedObject serialized = new SerializedObject(old);
            UnityEngine.Object shadow = serialized.FindProperty("shadow").objectReferenceValue;
            GameObject host = old.gameObject;
            UnityEngine.Object.DestroyImmediate(old);
            UIShadowLink replacement = host.AddComponent<UIShadowLink>();
            SerializedObject target = new SerializedObject(replacement);
            target.FindProperty("shadow").objectReferenceValue = shadow;
            target.ApplyModifiedPropertiesWithoutUndo();
        }

        foreach (UIButtonPressFeedback old in UnityEngine.Object.FindObjectsByType<UIButtonPressFeedback>(FindObjectsInactive.Include))
        {
            if (old.gameObject.scene != scene || HasScriptAsset(old)) continue;
            GameObject host = old.gameObject;
            UnityEngine.Object.DestroyImmediate(old);
            host.AddComponent<UIButtonPressFeedback>();
        }
    }

    static bool HasScriptAsset(MonoBehaviour behaviour)
    {
        MonoScript script = MonoScript.FromMonoBehaviour(behaviour);
        return script != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(script));
    }
}
