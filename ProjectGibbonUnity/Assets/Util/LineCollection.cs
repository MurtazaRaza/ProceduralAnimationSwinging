using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class DebugDraw
{
    private class LineCollection
    {
        private static readonly List<Vector3> vertices = new();
        private static readonly List<int> indices = new();
        private static readonly List<Color> colors = new();
        private static readonly int[] null_array = new int[0];
        public readonly List<DebugDrawLine> lines = new();
        public Mesh mesh;
        public MeshFilter mesh_filter;
        public GameObject mesh_object;
        public MeshRenderer mesh_renderer;

        public void Initialize(string obj_name, Material material)
        {
            mesh_object = new GameObject("DebugDrawLineMesh");
            mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh_renderer = mesh_object.AddComponent<MeshRenderer>();
            mesh_filter = mesh_object.AddComponent<MeshFilter>();
            mesh_filter.mesh = mesh;
            mesh_renderer.material = material;
        }

        public void StartFrame()
        {
            if (mesh_renderer != null)
            {
                if (lines.Count > 0)
                {
                    mesh_renderer.enabled = true;
                    vertices.Clear();
                    indices.Clear();
                    colors.Clear();
                    foreach (var line in lines)
                    {
                        indices.Add(vertices.Count);
                        vertices.Add(line.start);
                        colors.Add(line.start_color);
                        indices.Add(vertices.Count);
                        vertices.Add(line.end);
                        colors.Add(line.end_color);
                    }

                    mesh.SetIndices(null_array, MeshTopology.Lines, 0);
                    mesh.SetVertices(vertices);
                    mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
                    mesh.SetColors(colors);
                    mesh.RecalculateBounds();
                }
                else
                {
                    mesh_renderer.enabled = false;
                }
            }
        }

        public void EndFrame()
        {
            for (var i = lines.Count - 1; i >= 0; --i)
                if (lines[i].lifetime == Lifetime.OneFrame)
                    lines.RemoveAt(i);
        }

        public void EndFixedFrame()
        {
            for (var i = lines.Count - 1; i >= 0; --i)
                if (lines[i].lifetime == Lifetime.OneFixedUpdate)
                    lines.RemoveAt(i);
        }
    }
}