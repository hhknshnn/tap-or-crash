using UnityEngine;

public class PlanetSpawner : MonoBehaviour
{
    public GameObject planetPrefab;
    public GameObject[] planetPrefabs;  // Farklı gezegen tipleri
    public RocketController rocket;
    public float spawnDistance = 12f;
    public float verticalRange = 3f;
    public float minSize = 1.5f;
    public float maxSize = 2.5f;

    void Start()
    {
        // Başlangıçta sağda iki gezegen oluştur
        SpawnPlanet();
        SpawnPlanet();
    }

    public void SpawnPlanet()
    {
        Vector3 lastPos = rocket.planets.Count > 0
            ? rocket.planets[rocket.planets.Count - 1].position
            : Vector3.zero;

        float y = Random.Range(-verticalRange, verticalRange);
        Vector3 pos = lastPos + new Vector3(spawnDistance, y, 0);

        // Rastgele gezegen tipi seç
        GameObject selectedPrefab = planetPrefabs[Random.Range(0, planetPrefabs.Length)];
        GameObject p = Instantiate(selectedPrefab, pos, Quaternion.identity);
        
        // Skora göre gezegen boyutunu küçült
        int score = GameManager.instance.GetScore();
        float minS = Mathf.Max(0.2f, minSize - score * 0.005f);
        float maxS = Mathf.Max(0.3f, maxSize - score * 0.005f);
        float size = Random.Range(minS, maxS);
        
        p.transform.localScale = Vector3.one * size;
        p.tag = "Planet";

        rocket.AddPlanet(p.transform);
    }
}