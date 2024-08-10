using System.Collections.Generic;
using UnityEngine;

public partial class DebugDraw
{
    private class SphereCollection
    {
        public readonly List<DebugDrawWireSphere> spheres = new();
        public Material material;

        public void StartFrame()
        {
            foreach (var sphere in spheres)
            {
                var matrix = new Matrix4x4();
                matrix.SetTRS(sphere.center, sphere.rotation, sphere.scale);
                var material_property_block = new MaterialPropertyBlock();
                material_property_block.SetColor(instance.color_property_id, sphere.color);
                Graphics.DrawMesh(instance.sphere_mesh, matrix, material, instance.translucent_sphere_prefab.layer,
                    null, 0, material_property_block);
            }
        }

        public void EndFrame()
        {
            for (var i = spheres.Count - 1; i >= 0; --i)
                if (spheres[i].lifetime == Lifetime.OneFrame)
                    spheres.RemoveAt(i);
        }

        public void EndFixedFrame()
        {
            for (var i = spheres.Count - 1; i >= 0; --i)
                if (spheres[i].lifetime == Lifetime.OneFixedUpdate)
                    spheres.RemoveAt(i);
        }
    }
}