using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class CrashDebrisPresentation : MonoBehaviour
{
    private const float SequenceDuration = 0.74f;
    private static GameObject cachedDebrisPrefab;

    private sealed class Piece
    {
        public Transform transform;
        public Vector3 startPosition;
        public Quaternion startRotation;
        public Vector3 startScale;
        public Vector3 velocity;
        public Vector3 spin;
    }

    public static void Spawn(Vector3 position, Quaternion rocketRotation)
    {
        if (cachedDebrisPrefab == null)
            cachedDebrisPrefab = Resources.Load<GameObject>("VFX/RocketCrashDebris");
        if (cachedDebrisPrefab == null) return;

        GameObject root = Instantiate(cachedDebrisPrefab, position, rocketRotation);
        root.name = "RocketCrashDebris_Presentation";
        root.transform.localScale = Vector3.one * 0.42f;

        CrashDebrisPresentation presentation = root.AddComponent<CrashDebrisPresentation>();
        presentation.Begin();
    }

    void Begin()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        List<Piece> pieces = new List<Piece>(renderers.Length);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            renderer.sortingOrder = 18 + i;

            Transform pieceTransform = renderer.transform;
            Vector3 authoredOrigin = GetPieceOrigin(pieceTransform.name, i);
            pieceTransform.localPosition = authoredOrigin;
            Vector2 namedDirection = GetPieceDirection(pieceTransform.name, i);
            float speed = 1.55f + (i % 3) * 0.32f;
            pieces.Add(new Piece
            {
                transform = pieceTransform,
                startPosition = authoredOrigin,
                startRotation = pieceTransform.localRotation,
                startScale = pieceTransform.localScale,
                velocity = new Vector3(namedDirection.x, namedDirection.y, -0.12f + i * 0.045f) * speed,
                spin = new Vector3(110f + i * 27f, 150f - i * 18f, (i % 2 == 0 ? 1f : -1f) * (290f + i * 31f)),
            });
        }

        StartCoroutine(Animate(pieces));
    }

    IEnumerator Animate(List<Piece> pieces)
    {
        float elapsed = 0f;
        while (elapsed < SequenceDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float raw = Mathf.Clamp01(elapsed / SequenceDuration);
            float travel = 1f - Mathf.Pow(1f - raw, 2.25f);
            float lift = raw * raw * 0.36f;
            float shrink = raw < 0.66f
                ? 1f
                : Mathf.Lerp(1f, 0.02f, Mathf.SmoothStep(0f, 1f, (raw - 0.66f) / 0.34f));

            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                if (piece.transform == null) continue;

                piece.transform.localPosition = piece.startPosition
                    + piece.velocity * travel
                    + Vector3.down * lift;
                piece.transform.localRotation = piece.startRotation
                    * Quaternion.Euler(piece.spin * raw);
                piece.transform.localScale = piece.startScale * shrink;
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    static Vector2 GetPieceDirection(string pieceName, int index)
    {
        if (pieceName.Contains("WingLeft")) return new Vector2(-0.95f, 0.20f);
        if (pieceName.Contains("WingRight")) return new Vector2(0.95f, 0.24f);
        if (pieceName.Contains("Engine")) return new Vector2(-0.12f, -1f);
        if (pieceName.Contains("Nose")) return new Vector2(0.14f, 1f);

        float angle = 35f + index * 137.5f;
        return new Vector2(
            Mathf.Cos(angle * Mathf.Deg2Rad),
            Mathf.Sin(angle * Mathf.Deg2Rad)).normalized;
    }

    static Vector3 GetPieceOrigin(string pieceName, int index)
    {
        // FBX exporters differ in how they bake object offsets. These compact,
        // intentional origins keep the breakup matched to the gameplay rocket
        // while still using Blender-authored geometry and materials.
        if (pieceName.Contains("WingLeft")) return new Vector3(-0.24f, -0.02f, 0f);
        if (pieceName.Contains("WingRight")) return new Vector3(0.24f, -0.02f, 0f);
        if (pieceName.Contains("Engine")) return new Vector3(0f, -0.26f, 0.015f);
        if (pieceName.Contains("Nose")) return new Vector3(0f, 0.28f, -0.015f);
        return new Vector3(0.10f, 0.02f, -0.03f - index * 0.008f);
    }
}
