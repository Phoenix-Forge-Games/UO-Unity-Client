using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UOClient
{
    /// <summary>Accumulates flat-shaded, vertex-colored geometry into a single mesh. Reusable via <see cref="Clear"/>.</summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>(4096);
        private readonly List<Vector3> _normals = new List<Vector3>(4096);
        private readonly List<Color32> _colors = new List<Color32>(4096);
        private readonly List<int> _triangles = new List<int>(8192);

        public bool IsEmpty => _vertices.Count == 0;

        public void Clear()
        {
            _vertices.Clear();
            _normals.Clear();
            _colors.Clear();
            _triangles.Clear();
        }

        /// <summary>Adds a quad from four corners in clockwise order seen from the front.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color)
        {
            Vector3 normal = Vector3.Cross(b - a, d - a).normalized;
            int start = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _vertices.Add(d);
            for (int i = 0; i < 4; i++)
            {
                _normals.Add(normal);
                _colors.Add(color);
            }

            _triangles.Add(start);
            _triangles.Add(start + 1);
            _triangles.Add(start + 2);
            _triangles.Add(start);
            _triangles.Add(start + 2);
            _triangles.Add(start + 3);
        }

        /// <summary>Adds an axis-aligned box; the bottom face is skipped since nothing looks up at it.</summary>
        public void AddBox(Vector3 min, Vector3 max, Color32 color)
        {
            var p000 = new Vector3(min.x, min.y, min.z);
            var p100 = new Vector3(max.x, min.y, min.z);
            var p010 = new Vector3(min.x, max.y, min.z);
            var p110 = new Vector3(max.x, max.y, min.z);
            var p001 = new Vector3(min.x, min.y, max.z);
            var p101 = new Vector3(max.x, min.y, max.z);
            var p011 = new Vector3(min.x, max.y, max.z);
            var p111 = new Vector3(max.x, max.y, max.z);

            Color32 side = Shade(color, 0.85f);
            AddQuad(p010, p011, p111, p110, color); // top
            AddQuad(p000, p010, p110, p100, side); // -z
            AddQuad(p101, p111, p011, p001, side); // +z
            AddQuad(p001, p011, p010, p000, side); // -x
            AddQuad(p100, p110, p111, p101, side); // +x
        }

        public void ApplyTo(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
        }

        public static Color32 Shade(Color32 color, float factor) =>
            new Color32((byte)(color.r * factor), (byte)(color.g * factor), (byte)(color.b * factor), color.a);
    }
}
