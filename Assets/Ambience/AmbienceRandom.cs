using UnityEngine;

// Gezegen kimliğinden tohumlanan, oyunun global Random durumuna dokunmayan
// hafif üreteç. Aynı tohum her çalışmada aynı yerleşimi verir; böylece her
// gezegen "el yapımı" gibi sabit bir kimliğe sahip olur.
public sealed class AmbienceRandom
{
    uint state;

    public AmbienceRandom(int seed)
    {
        unchecked { state = (uint)seed * 2654435761u + 0x9E3779B9u; }
        if (state == 0u) state = 0x6D2B79F5u;
    }

    // 0..1 aralığında bir sonraki değer (xorshift32).
    public float Value
    {
        get
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / (float)0x1000000;
        }
    }

    public float Range(float min, float max) => min + (max - min) * Value;

    public int Range(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        int span = maxExclusive - minInclusive;
        return minInclusive + Mathf.Min((int)(Value * span), span - 1);
    }

    public bool Chance(float probability) => Value < probability;

    public float Sign => Value < 0.5f ? -1f : 1f;

    public float Angle => Value * Mathf.PI * 2f;

    public Vector2 OnCircle(float radius)
    {
        float angle = Angle;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    // Disk içinde düzgün dağılım (merkeze yığılmaz).
    public Vector2 InsideDisk(float radius) => OnCircle(radius * Mathf.Sqrt(Value));
}
