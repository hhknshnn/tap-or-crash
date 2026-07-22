using UnityEngine;
using System.Collections;

public class ShareManager : MonoBehaviour
{
    // Paylaşım mesajının skoru içerdiği kısım dinamik olarak doldurulur
    private string GetShareMessage()
    {
        int score = GameManager.instance.GetScore();
        return score + " puan benim — şimdi sıra sende! 🚀 Tap or Crash'i dene ve geç bakalım!";
    }

    // Share butonuna bağlanacak ana fonksiyon
    public void OnShareButtonClicked()
    {
        StartCoroutine(TakeScreenshotAndShare());
    }

    IEnumerator TakeScreenshotAndShare()
    {
        // UI'ın render edilmesi için bir frame bekle
        yield return new WaitForEndOfFrame();

        // Ekran görüntüsü al
        Texture2D screenshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        screenshot.Apply();

        // Geçici dosya yolu oluştur
        string filePath = System.IO.Path.Combine(Application.temporaryCachePath, "screenshot.png");
        
        // Ekran görüntüsünü dosyaya kaydet
        System.IO.File.WriteAllBytes(filePath, screenshot.EncodeToPNG());
        
        // Texture'ı bellekten temizle — bellek sızıntısını önler
        Destroy(screenshot);

        // NativeShare ile paylaş — hem görsel hem metin
        new NativeShare()
            .AddFile(filePath)
            .SetText(GetShareMessage())
            .Share();
    }
}