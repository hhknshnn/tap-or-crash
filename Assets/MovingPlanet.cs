using UnityEngine;

// PlanetSpawner tarafından belirli gezegenlere eklenir.
// Gezegeni yatayda sinüs dalgasıyla hareket ettirir — roket yakalamayı zorlaştırır.
public class MovingPlanet : MonoBehaviour
{
    [SerializeField] private float amplitude = 0.35f;
    [SerializeField] private float frequency = 0.18f;

    private Vector3 origin;
    private float   timeOffset; // Her gezegen farklı fazda başlasın

    void Start()
    {
        origin     = transform.position;
        timeOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    public void Configure(float difficulty)
    {
        float t = Mathf.Clamp01(difficulty);
        amplitude = Mathf.Lerp(0.35f, 1.0f, t);
        frequency = Mathf.Lerp(0.18f, 0.48f, t);
    }

    void Update()
    {
        if (GameManager.isGameOver) return;

        float x = origin.x + Mathf.Sin((Time.time * frequency * Mathf.PI * 2f) + timeOffset) * amplitude;
        transform.position = new Vector3(x, origin.y, origin.z);
    }
}
