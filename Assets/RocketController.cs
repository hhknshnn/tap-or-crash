using UnityEngine;
using System.Collections.Generic;

public class RocketController : MonoBehaviour
{
    [Header("Trajectory")]
    public LineRenderer trajectoryLine;    // Yön gösterge çizgisi
    public int trajectoryPoints = 10;      // Kaç nokta gösterilsin
    public float trajectoryLength = 5f;    // Çizgi uzunluğu

    [Header("Orbit Ayarları")]
    private float orbitSpeed = 120f;        // Dönüş hızı
    public float orbitRadius = 3f;         // Gezegenden uzaklık

    [Header("Uçuş Ayarları")]
    public float flightSpeed = 8f;         // Uçuş hızı
    public float captureRadius = 2.5f;     // Gezegene bu kadar yaklaşınca yakalanır

    [Header("Zorluk Ayarları")]
    private float baseOrbitSpeed = 120f;   // Başlangıç dönüş hızı
    public float speedIncreaseRate = 2f;   // Her skorada hız artışı
    private int orbitDirection = 1;         // 1 = saat yönü, -1 = tersi
    // Gezegen listesi
    public List<Transform> planets = new List<Transform>();
    private int currentIndex = 0;

    // Durum
    private bool isOrbiting = true;
    private float angle = 0f;
    private Vector3 flyDir;

    void Update()
    {
        if (GameManager.isGameOver) return;
        if (planets.Count == 0) return;

        if (isOrbiting)
        {
            DoOrbit();
            DrawTrajectory();

            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
                DoLaunch();
        }
        else
        {
            DoFlight();
        }
    }

    void DoOrbit()
    {
        // Skora göre hızı artır
        orbitSpeed = baseOrbitSpeed + (GameManager.instance.GetScore() * speedIncreaseRate);

        // Dönüş yönüne göre açıyı artır
        angle += orbitSpeed * orbitDirection * Time.deltaTime;

        Vector3 center = planets[currentIndex].position;
        float rad = angle * Mathf.Deg2Rad;
        transform.position = center + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0) * orbitRadius;
    }

    void DrawTrajectory()
    {
        // Şimdilik devre dışı — görseller eklenince roket ateş efekti gelecek
        trajectoryLine.positionCount = 0;
    }

    void DoLaunch()
    {
        // Her durumda önce çizgiyi gizle
        trajectoryLine.positionCount = 0;

        // İlk fırlatmada hep sağa git
        if (currentIndex == 0 && planets.Count > 1)
        {
            flyDir = (planets[1].position - transform.position).normalized;
            isOrbiting = false;
            FindObjectOfType<PlanetSpawner>().SpawnPlanet();
            return;
        }

        // O anki teğet yönünde fırlat
        Vector3 center = planets[currentIndex].position;
        Vector3 toRocket = (transform.position - center).normalized;
        flyDir = new Vector3(-toRocket.y, toRocket.x, 0).normalized;
        // Çubukla aynı yönde — hep sağa fırlat
        if (flyDir.x < 0) flyDir = -flyDir; 
        // Perfect window kontrolü
        if (currentIndex + 1 < planets.Count)
        {
            Transform next = planets[currentIndex + 1];
            Vector3 toNext = (next.position - transform.position).normalized;
            float dot = Vector3.Dot(flyDir, toNext);

            if (dot > 0.99f)
            {
                GameManager.instance.PerfectLaunch();
            }
        }

        isOrbiting = false;
    }

    void DoFlight()
    {
        // Düz uç
        transform.position += flyDir * flightSpeed * Time.deltaTime;

        // Sonraki gezegene yaklaştık mı?
        if (currentIndex + 1 < planets.Count)
        {
            Transform next = planets[currentIndex + 1];
            float dist = Vector3.Distance(transform.position, next.position);

            if (dist < captureRadius)
            {
                // Yakalandık!
                currentIndex++;
                isOrbiting = true;
                angle = Mathf.Atan2(
                    transform.position.y - next.position.y,
                    transform.position.x - next.position.x
                ) * Mathf.Rad2Deg;

                // Yeni gezegen spawn et ve skoru artır
                FindObjectOfType<PlanetSpawner>().SpawnPlanet();
                // Yeni gezegene geçince rastgele dönüş yönü değiştir
                orbitDirection = Random.Range(0, 2) == 0 ? 1 : -1;
                GameManager.instance.AddScore();
                return;
            }
        }

        // Çok uzaklaştık mı? Game Over
        float distToCurrent = Vector3.Distance(transform.position, planets[currentIndex].position);
        if (distToCurrent > 18f)
        {
            GameManager.instance.TriggerGameOver();
        }
    }

    // Dışarıdan gezegen eklemek için
    public void AddPlanet(Transform t)
    {
        planets.Add(t);
    }
}