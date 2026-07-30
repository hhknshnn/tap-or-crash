# Planet Ambience

Gezegenlere tema bazlı, **tamamen dekoratif** görsel katman ekler.

## Sözleşme

- Üretilen hiçbir nesnede collider, trigger veya oyun tag'i yoktur.
- Gezegenin kendi `SpriteRenderer` sınırları değişmez (yalnızca `color` tonlanır),
  bu yüzden yörünge yarıçapı ve yakalama trigger'ı etkilenmez.
- Parçacık sistemlerinde `collision` ve `trigger` modülleri kapalıdır.
- Sıralama `planet.sortingOrder + 1..2`; roket (10) ve HUD efektleri (12) her zaman üstte kalır.
- Ekran dışındaki gezegen parçacık üretmez (`Animate(time, visible)`).

## Dosyalar

| Dosya | İş |
|---|---|
| `PlanetAmbience.cs` | Taban sınıf + tema kaydı + gezegen kimliği (`PlanetIndex`, `Variant`). |
| `PlanetAmbienceKit.cs` | Temaların paylaştığı hazır efektler (hâle, parıltı, uçucu, sürüklenme...). |
| `AmbienceVfxAssets.cs` | Çalışma zamanında üretilen 64×64 siluetler + paylaşılan parçacık malzemeleri. |
| `AmbienceRandom.cs` | Gezegen kimliğinden tohumlanan kararlı üreteç (global `Random`'a dokunmaz). |
| `NaturalPlanetAmbience.cs` | Yeşil paletin 10 farklı ruh hâli + 10 farklı efekt bileşimi. |
| `IcePlanetAmbience.cs` | Buzul paletinin 10 farklı kimliği + 10 farklı efekt bileşimi. |
| `LavaPlanetAmbience.cs` | Volkanik tema (kendi özel efektleriyle, taban sınıf üzerine). |

## Yeni tema ekleme

Oyun kodunda (`PlanetPresentation`, `PlanetSpawner`, `RocketController`, HUD) hiçbir
değişiklik gerekmez. Tek yapılacak, bu klasöre bir dosya eklemek:

```csharp
public sealed class DesertPlanetAmbience : PlanetAmbienceKit
{
    static readonly Color AuraTint = new Color(1f, 0.82f, 0.45f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterTheme()
    {
        Register(PlanetAmbienceTheme.ByNamePrefix<DesertPlanetAmbience>("Desert", "Desert", AuraTint));
    }

    protected override void Build()
    {
        // Gezegen başına el yapımı kimlik: PlanetIndex tablodan bir palet seçer.
        MultiplyTint(new Color(1f, 0.94f, 0.78f));

        AddDrift(new DriftSettings { sprite = AmbienceVfxAssets.SoftDot, mode = DriftMode.Gust, ... });
        AddSheen(Color.white, 0.12f, 1.6f, 3f, 7f);
    }
}
```

Eşleşme prefab/sprite adına bakar (`Desert_01`, `Desert_02(Clone)`...). Farklı bir kural
gerekiyorsa `PlanetAmbienceTheme` yapıcısına kendi `Func<string, bool>` matcher'ını ver.

Tema adı (`"Natural"`, `"Ice"`, `"Lava"`) `PlanetSpawner.levels[].levelName` ile aynı
yazılırsa HUD (`LevelProgressUI`) vurgu rengini
`PlanetAmbience.AccentColorFor` üzerinden kendiliğinden alır.

## Taban sınıfın verdikleri

| Üye | İş |
|---|---|
| `LocalRadius` | Gezegen yarıçapı, yerel birim. Tüm boyut/hızları bunun katı olarak yaz. |
| `Phase` | Gezegen başına sabit rastgele faz; nabızlar senkron görünmesin. |
| `PlanetIndex` | Sprite adındaki numara (`Natural_03` → 3); palet tablosunun anahtarı. |
| `Variant` | Gezegen tipine bağlı kararlı rastgelelik: aynı gezegen her zaman aynı yerleşim. |
| `PlanetVisible` | Ekran dışında parçacık üretmemek için. |
| `MultiplyTint(color)` | Sprite rengini çarparak tonlar. |
| `CreateSprite(...)` | Collider'sız dekoratif sprite katmanı. |
| `CreateDecorativeParticles(...)` | Elle `Emit` ile beslenen, çarpışmasız parçacık sistemi (malzeme verilebilir). |
| `Emit(...)` | Yerel konum/hız ile tek parçacık. |
| `DistanceToRim(p, d)` | Efektlerin disk dışına taşmaması için. |
| `Spread(dir, deg)` / `RandomOffset(r)` | Yön/konum saçılımı. |

## Kitin verdiği efektler (`PlanetAmbienceKit`)

| Kurucu | Görsel |
|---|---|
| `AddHalo(...)` | Gezegenin arkasında nefes alan yumuşak hâle. |
| `AddTwinkles(...)` | Yüzeyde sırayla parlayıp sönen noktalar (kristal, çiy). |
| `AddOrbiters(...)` | Çevrede dolanan minik canlılar (kelebek, böcek); isteğe bağlı kanat çırpma. |
| `AddCrossing(...)` | Ara sıra önden geçen uçucu (kuş). Tek nesne tekrar kullanılır, havuzlanmıştır. |
| `AddSheen(...)` | Disk üzerinden süzülen ince ışık/rüzgâr perdesi. |
| `AddBreath(...)` | Sprite renginin çok hafif nefes alması (rüzgârda çimen, buz parıltısı). |
| `AddDrift(DriftSettings)` | Sürüklenen parçacık alanı: `Fall` (yaprak/kar), `Float` (polen/buz tozu), `Gust` (ani rüzgâr). |

Alt sınıflar `Start`/`Update`/`Animate` **tanımlamaz**; `Build()` ile efektleri kurar,
gerekirse `OnAnimate()` ile temaya özel ek hareket yazar.

## Performans

- Şekiller (yaprak, taç yaprağı, kelebek, kuş, kar tanesi, kıymık, parıltı) çalışma
  zamanında bir kez üretilir; doku CPU kopyası `Apply(false, true)` ile bırakılır.
- Aynı şekli kullanan tüm gezegenler tek malzemeyi paylaşır (`ParticleMaterialFor`).
- Parçacıklar elle `Emit` edilir; gezegen ekran dışındayken hiç üretim olmaz.
- Gezegen başına 2–3 efekt, efekt başına ≤ 16 parçacık hedeflenir.
