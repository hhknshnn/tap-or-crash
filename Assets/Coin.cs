using UnityEngine;
using System.Collections;

// Dünyada dolaşan coin pickup.
// Prefab'a ekle: SpriteRenderer (sarı daire) + bu script.
// CoinManager.AwardCoinForPlanet() tarafından gezegen pozisyonuna spawn edilir
// ya da direkt Instantiate ile kullanılabilir.
public class Coin : MonoBehaviour
{
    [SerializeField] private float bobAmplitude  = 0.15f; // Yukarı-aşağı salınım yüksekliği
    [SerializeField] private float bobFrequency  = 2f;    // Saniyedeki salınım sayısı
    [SerializeField] private float magnetRadius  = 6f;    // Bu mesafede rokete doğru çekilir
    [SerializeField] private float collectRadius = 1.2f;  // Bu mesafede toplanır
    [SerializeField] private float magnetSpeed   = 8f;    // Manyetik çekim hızı
    [SerializeField] private float lifetime      = 6f;    // Toplanmadan yok olma süresi

    private Vector3       startPos;
    private Transform     rocket;
    private bool          collected = false;

    void Start()
    {
        startPos = transform.position;
        RocketController rc = FindAnyObjectByType<RocketController>();
        if (rc != null) rocket = rc.transform;

        StartCoroutine(AutoDestroy());
    }

    void Update()
    {
        if (collected || GameManager.isGameOver) return;
        if (rocket == null) return;

        float dist = Vector2.Distance(transform.position, rocket.position);

        if (dist < collectRadius)
        {
            Collect();
            return;
        }

        if (dist < magnetRadius)
        {
            // Manyetik çekim
            transform.position = Vector3.MoveTowards(
                transform.position, rocket.position,
                magnetSpeed * Time.deltaTime);
        }
        else
        {
            // Salınım hareketi
            float y = startPos.y + Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
            transform.position = new Vector3(startPos.x, y, startPos.z);
        }
    }

    void Collect()
    {
        if (collected) return;
        collected = true;
        StartCoroutine(CollectAnim());
    }

    IEnumerator CollectAnim()
    {
        float t = 0f;
        while (t < 0.15f)
        {
            t += Time.deltaTime;
            float p = t / 0.15f;
            transform.localScale = Vector3.one * Mathf.Lerp(1f, 0f, p);
            yield return null;
        }
        Destroy(gameObject);
    }

    IEnumerator AutoDestroy()
    {
        yield return new WaitForSeconds(lifetime);
        if (!collected) Destroy(gameObject);
    }

    // CoinManager veya dışarıdan çağrılacak spawn yardımcısı
    public static void SpawnAt(Vector3 worldPos, GameObject prefab)
    {
        if (prefab == null) return;
        // Hafif rastgele ofset — tam gezegen merkezinde değil
        Vector3 offset = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.3f, 0.8f), 0f);
        Instantiate(prefab, worldPos + offset, Quaternion.identity);
    }
}
