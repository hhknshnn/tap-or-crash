using UnityEngine;

public class ParallaxBackground : MonoBehaviour
{
    private Transform cam;
    public float parallaxSpeed = 0.1f;

    private float spriteHeight;         // Sprite yüksekliği (dikey oyun için)
    private Vector3 startPos;

    void Start()
    {
        cam = Camera.main.transform;
        spriteHeight = GetComponent<SpriteRenderer>().bounds.size.y;
        Debug.Log("Sprite Height: " + spriteHeight); // Gerçek değeri görmek için // Yüksekliği al
        startPos = transform.position;
    }

    void LateUpdate()
    {
        // Kameranın Y eksenindeki hareketine göre parallax uygula
        float camTravelled = cam.position.y;
        float newY = startPos.y + camTravelled * parallaxSpeed;
        transform.position = new Vector3(startPos.x, newY, startPos.z);

        // Kamera sprite'ın üst sınırını geçtiyse yukarı atla
        if (cam.position.y > transform.position.y + spriteHeight * 0.5f)
            startPos.y += spriteHeight;
        else if (cam.position.y < transform.position.y - spriteHeight * 0.5f)
            startPos.y -= spriteHeight;
    }
}