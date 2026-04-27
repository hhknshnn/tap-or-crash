using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // Takip edilecek nesne (Rocket)
    public float smoothSpeed = 5f; // Kameranın takip yumuşaklığı (yüksek = daha hızlı)

    void LateUpdate()
    {
        if (target == null) return;

        // Sadece Y eksenini takip et — dikey oyun için X sabit kalır
        Vector3 targetPos = new Vector3(0f, target.position.y, -10f);

        transform.position = Vector3.Lerp(transform.position, targetPos, smoothSpeed * Time.deltaTime);
    }
}