using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Sahnedeki (Inspector'dan atanmış) butonları modern stile dönüştürür.
// Canvas altındaki herhangi bir objeye ekle — Start()'ta otomatik çalışır.
public class SceneButtonStyler : MonoBehaviour
{
    void Start()
    {
        Canvas canvas = UIRootCanvas.Resolve();
        if (canvas == null) return;

        StyleNamedButton(canvas, "RestartButton",            UIStyleKit.BtnAccent);
        StyleNamedButton(canvas, "ShareButton",              UIStyleKit.BtnPrimary);
        StyleNamedButton(canvas, "MainMenuButton_GameOver",  UIStyleKit.BtnNeutral);
        StyleNamedButton(canvas, "GotItButton",              UIStyleKit.BtnSuccess);
        StyleNamedButton(canvas, "PauseButton",              UIStyleKit.BtnNeutral);

        // Tüm TextMeshPro metinlerinin rengini temaya uyarla (buton içindeki)
        StyleButtonLabels(canvas);
    }

    static void StyleNamedButton(Canvas canvas, string goName, Color color)
    {
        Transform t = canvas.transform.Find(goName);
        if (t == null) t = FindDeep(canvas.transform, goName);
        if (t == null) return;

        Button btn = t.GetComponent<Button>();
        if (btn != null) UIStyleKit.ResyleButton(btn, color);

        // Buton içindeki TMP text rengini beyaz yap
        foreach (var tmp in t.GetComponentsInChildren<TextMeshProUGUI>())
            tmp.color = UIStyleKit.TextMain;
    }

    static void StyleButtonLabels(Canvas canvas)
    {
        foreach (var btn in canvas.GetComponentsInChildren<Button>(true))
        {
            foreach (var tmp in btn.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                // Yalnızca saf siyah veya çok koyu metinleri düzelt
                if (tmp.color.r < 0.15f && tmp.color.g < 0.15f && tmp.color.b < 0.15f)
                    tmp.color = UIStyleKit.TextMain;
            }
        }
    }

    // Canvas altında ad ile derinlemesine ara
    static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
