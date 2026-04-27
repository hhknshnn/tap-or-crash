using UnityEngine;
using System.Collections.Generic;

public class RocketController : MonoBehaviour
{
    [Header("Trajectory")]
    public LineRenderer trajectoryLine;    // Yön gösterge çizgisi (şu an devre dışı)
    public int trajectoryPoints = 10;      // Kaç nokta gösterilsin (şu an kullanılmıyor)
    public float trajectoryLength = 5f;    // Çizgi uzunluğu (şu an kullanılmıyor)

    [Header("Roket Efekti")]
    public ParticleSystem thrusterParticles;          // Raketin altındaki ateş/alev particle sistemi
    [SerializeField] private float orbitEmissionRate = 15f;  // Orbit dönerken saniyede kaç partikül çıksın
    [SerializeField] private float flightEmissionRate = 45f; // Uçuş sırasında saniyede kaç partikül çıksın

    [Header("Orbit Ayarları")]
    private float orbitSpeed = 120f;       // Şu anki dönüş hızı (runtime'da değişir)
    public float orbitRadius = 3f;         // Gezegene olan mesafe (yörünge yarıçapı)

    [Header("Uçuş Ayarları")]
    public float flightSpeed = 8f;         // Fırlatınca raketin düz uçuş hızı
    public float captureRadius = 2.5f;     // Sonraki gezegene bu kadar yaklaşınca yakalanma tetiklenir

    [Header("Zorluk Ayarları")]
    private float baseOrbitSpeed = 120f;   // Oyun başındaki temel dönüş hızı
    public float speedIncreaseRate = 2f;   // Her skor artışında orbit hızına eklenen miktar
    private int orbitDirection = 1;        // Dönüş yönü: 1 = saat yönü, -1 = saat yönü tersi

    public List<Transform> planets = new List<Transform>(); // Sahnedeki tüm gezegenlerin listesi
    private int currentIndex = 0;          // Şu an hangi gezegenin etrafında dönülüyor

    private bool isOrbiting = true;        // true = orbit, false = uçuş
    private float angle = 0f;             // Şu anki orbit açısı (derece)
    private Vector3 flyDir;               // Fırlatma anındaki uçuş yönü

    void Update()
    {
        if (GameManager.isGameOver) return; // Oyun bittiyse hiçbir şey yapma
        if (planets.Count == 0) return;     // Gezegen yoksa hiçbir şey yapma

        if (isOrbiting)
        {
            DoOrbit();       // Gezegen etrafında dön
            DrawTrajectory(); // Yön çizgisini güncelle (şu an boş)

            // Dokunuş veya Space'e basınca fırlat
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
                DoLaunch();
        }
        else
        {
            DoFlight(); // Düz uç
        }
    }

    void DoOrbit()
    {
        // Skoru kullanarak orbit hızını güncelle — skor arttıkça roket daha hızlı döner
        orbitSpeed = baseOrbitSpeed + (GameManager.instance.GetScore() * speedIncreaseRate);

        // Açıyı her frame artır — bu raketin gezegen etrafında dönmesini sağlar
        angle += orbitSpeed * orbitDirection * Time.deltaTime;

        // Açıya göre raketin yeni pozisyonunu hesapla (trigonometri ile daire hareketi)
        Vector3 center = planets[currentIndex].position;
        float rad = angle * Mathf.Deg2Rad; // Derece → Radyan dönüşümü (Unity trig fonksiyonları radyan ister)
        transform.position = center + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0) * orbitRadius;

        // Orbit modunda partikül yoğunluğunu düşük tut
        SetEmissionRate(orbitEmissionRate);
    }

    void DrawTrajectory()
    {
        // Şimdilik devre dışı — ileride roket ateş efektiyle değiştirilecek
        trajectoryLine.positionCount = 0;
    }

    void DoLaunch()
    {
        // Yön çizgisini gizle
        trajectoryLine.positionCount = 0;

        // Fırlatınca partikül yoğunluğunu artır — görsel olarak "itme" hissi verir
        SetEmissionRate(flightEmissionRate);

        // İlk fırlatmada her zaman ikinci gezegene doğru git
        if (currentIndex == 0 && planets.Count > 1)
        {
            flyDir = (planets[1].position - transform.position).normalized; // İkinci gezegene bakan yön
            isOrbiting = false;
            FindObjectOfType<PlanetSpawner>().SpawnPlanet(); // Yeni gezegen üret
            return;
        }

        // Mevcut orbit yönünün teğet vektörünü hesapla (dairenin üzerindeki anlık hareket yönü)
        Vector3 center = planets[currentIndex].position;
        Vector3 toRocket = (transform.position - center).normalized; // Gezegenden rokete bakan birim vektör
        flyDir = new Vector3(-toRocket.y, toRocket.x, 0).normalized; // 90 derece döndür → teğet yön

        // Her zaman yukarı doğru uç (dikey oyun için)
        if (flyDir.y < 0) flyDir = -flyDir;

        // Sonraki gezegene tam doğru fırlatıldı mı? (Perfect kontrolü)
        if (currentIndex + 1 < planets.Count)
        {
            Transform next = planets[currentIndex + 1];
            Vector3 toNext = (next.position - transform.position).normalized; // Sonraki gezegene bakan yön
            float dot = Vector3.Dot(flyDir, toNext); // İki yönün ne kadar hizalı olduğu (1 = tam hizalı)

            if (dot > 0.99f) // Neredeyse mükemmel hizalanma
            {
                GameManager.instance.PerfectLaunch(); // Bonus puan
            }
        }

        isOrbiting = false; // Uçuş moduna geç
    }

    void DoFlight()
    {
        // Her frame'de uçuş yönünde ilerle
        transform.position += flyDir * flightSpeed * Time.deltaTime;

        // Sonraki gezegen var mı ve yaklaştık mı?
        if (currentIndex + 1 < planets.Count)
        {
            Transform next = planets[currentIndex + 1];
            float dist = Vector3.Distance(transform.position, next.position);

            if (dist < captureRadius) // Yakalanma mesafesine girdik
            {
                currentIndex++;        // Bir sonraki gezegene geç
                isOrbiting = true;     // Orbit moduna dön
                SetEmissionRate(orbitEmissionRate); // Partikül yoğunluğunu düşür

                // Raketin şu anki açısını hesapla — orbit'e yumuşak giriş için
                angle = Mathf.Atan2(
                    transform.position.y - next.position.y,
                    transform.position.x - next.position.x
                ) * Mathf.Rad2Deg;

                FindObjectOfType<PlanetSpawner>().SpawnPlanet(); // Yeni gezegen üret
                orbitDirection = Random.Range(0, 2) == 0 ? 1 : -1; // Rastgele dönüş yönü seç
                GameManager.instance.AddScore(); // Skoru artır
                return;
            }
        }

        // Mevcut gezegenden çok uzaklaştıysa oyun biter
        float distToCurrent = Vector3.Distance(transform.position, planets[currentIndex].position);
        if (distToCurrent > 12f)
        {
            GameManager.instance.TriggerGameOver();
        }
    }

    // Dışarıdan yeni gezegen eklemek için (PlanetSpawner tarafından çağrılır)
    public void AddPlanet(Transform t)
    {
        planets.Add(t);
    }

    // Particle System'in saniyedeki partikül sayısını ayarla
    void SetEmissionRate(float rate)
    {
        if (thrusterParticles == null) return; // Particle System atanmamışsa sessizce geç, hata verme
        var emission = thrusterParticles.emission; // Emission modülünü al (struct olduğu için var ile alıyoruz)
        emission.rateOverTime = rate;              // Saniyedeki partikül sayısını güncelle
    }
}   