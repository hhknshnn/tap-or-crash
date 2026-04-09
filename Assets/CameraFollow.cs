using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // Takip edilecek nesne (Rocket)
    public float smoothSpeed = 5f; // Kameranın takip yumuşaklığı (yüksek = daha hızlı)

    void LateUpdate()
    {
        // Target atanmamışsa hiçbir şey yapma
        if (target == null) return;

        // Hedefin pozisyonunu al, Z eksenini -10 yap (kamera arkada kalsın)
        Vector3 targetPos = new Vector3(target.position.x, target.position.y, -10f);

        // Kamerayı yumuşakça hedefe doğru hareket ettir
        transform.position = Vector3.Lerp(transform.position, targetPos, smoothSpeed * Time.deltaTime);
    }
}