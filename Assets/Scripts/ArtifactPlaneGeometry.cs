using Unity.Collections;
using UnityEngine;

namespace Urp.ArDemo
{
    public static class ArtifactPlaneGeometry
    {
        public static float Area(NativeArray<Vector2> boundary)
        {
            if (!boundary.IsCreated || boundary.Length < 3) return 0f;
            float twiceArea = 0f;
            for (int i = 0; i < boundary.Length; i++)
            {
                Vector2 a = boundary[i];
                Vector2 b = boundary[(i + 1) % boundary.Length];
                twiceArea += a.x * b.y - b.x * a.y;
            }
            return Mathf.Abs(twiceArea) * .5f;
        }

        public static bool WithinOrNearBoundary(NativeArray<Vector2> boundary, Vector2 point, float margin)
        {
            if (!boundary.IsCreated || boundary.Length < 3) return false;
            bool inside = false;
            float nearestSquared = float.PositiveInfinity;
            for (int i = 0; i < boundary.Length; i++)
            {
                Vector2 a = boundary[i];
                Vector2 b = boundary[(i + 1) % boundary.Length];
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
                Vector2 edge = b - a;
                float lengthSquared = edge.sqrMagnitude;
                float fraction = lengthSquared > 0f
                    ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / lengthSquared) : 0f;
                nearestSquared = Mathf.Min(nearestSquared, (point - (a + edge * fraction)).sqrMagnitude);
            }
            return inside || nearestSquared <= margin * margin;
        }
    }
}
