using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
#if UNITY_EDITOR

public class ScriptOrder : Attribute
{
    public int order;

    public ScriptOrder(int order)
    {
        this.order = order;
    }
}

[InitializeOnLoad]
public class ScriptOrderManager
{
    static ScriptOrderManager()
    {
        foreach (var monoScript in MonoImporter.GetAllRuntimeMonoScripts())
            if (monoScript.GetClass() != null)
                foreach (var a in Attribute.GetCustomAttributes(monoScript.GetClass(), typeof(ScriptOrder)))
                {
                    var currentOrder = MonoImporter.GetExecutionOrder(monoScript);
                    var newOrder = ((ScriptOrder)a).order;
                    if (currentOrder != newOrder)
                        MonoImporter.SetExecutionOrder(monoScript, newOrder);
                }
    }
}

[ScriptOrder(-100)]
#endif
public partial class DebugDraw : MonoBehaviour
{
    public enum Lifetime
    {
        Persistent,
        OneFrame,
        OneFixedUpdate
    }

    public enum Type
    {
        Normal,
        Xray
    }

    private static DebugDraw instance;
    public GameObject translucent_sphere_prefab;
    public Material line_material;
    public Material line_material_xray;

    private readonly LineCollection lines = new();
    private readonly LineCollection lines_xray = new();
    private readonly SphereCollection spheres = new();
    private readonly SphereCollection spheres_xray = new();

    private readonly WaitForEndOfFrame wait_for_end_of_frame = new();
    private int color_property_id;

    private Mesh sphere_mesh;

    private void Awake()
    {
        instance = this;

        lines.Initialize("DebugDrawLineMesh", line_material);
        lines_xray.Initialize("DebugDrawLineMeshXRay", line_material_xray);
        spheres.material = line_material;
        spheres_xray.material = line_material_xray;
        sphere_mesh = translucent_sphere_prefab.GetComponent<MeshFilter>().sharedMesh;

        color_property_id = Shader.PropertyToID("_TintColor");
    }

    private void FixedUpdate()
    {
        lines.EndFixedFrame();
        lines_xray.EndFixedFrame();
        spheres.EndFixedFrame();
        spheres_xray.EndFixedFrame();
    }

    private void LateUpdate()
    {
        lines.StartFrame();
        lines_xray.StartFrame();
        spheres.StartFrame();
        spheres_xray.StartFrame();
        StartCoroutine("Clear");
    }

    public static void Remove(DebugDrawLine to_remove)
    {
        instance.lines.lines.Remove(to_remove);
        instance.lines_xray.lines.Remove(to_remove);
    }

    public static void Remove(DebugDrawWireSphere to_remove)
    {
        instance.spheres.spheres.Remove(to_remove);
        instance.spheres_xray.spheres.Remove(to_remove);
    }

    public static DebugDrawWireSphere Sphere(Vector3 center, Color color, Vector3 scale, Quaternion rotation,
        Lifetime lifetime, Type type)
    {
        var sphere = new DebugDrawWireSphere();
        sphere.center = center;
        sphere.color = color;
        sphere.scale = scale;
        sphere.rotation = rotation;
        sphere.lifetime = lifetime;
        if (type == Type.Xray)
            instance.spheres_xray.spheres.Add(sphere);
        else
            instance.spheres.spheres.Add(sphere);
        return sphere;
    }

    public static DebugDrawLine Line(Vector3 start, Vector3 end, Color color, Lifetime lifetime, Type type)
    {
        return Line(start, end, color, color, lifetime, type);
    }

    public static DebugDrawLine Line(Vector3 start, Vector3 end, Color start_color, Color end_color, Lifetime lifetime,
        Type type)
    {
        var line = new DebugDrawLine();
        line.start = start;
        line.end = end;
        line.start_color = start_color;
        line.end_color = end_color;
        line.lifetime = lifetime;
        if (type == Type.Xray)
            instance.lines_xray.lines.Add(line);
        else
            instance.lines.lines.Add(line);
        return line;
    }

    public static void Box(Vector3 center, Color color, Vector3 scale, Quaternion rotation, Lifetime lifetime,
        Type type)
    {
        var corner = scale * 0.5f;
        // Top
        Line(center + rotation * Vector3.Scale(corner, new Vector3(-1f, 1f, 1f)),
            center + rotation * Vector3.Scale(corner, new Vector3(1f, 1f, 1f)),
            color, lifetime, type);
        Line(center + rotation * Vector3.Scale(corner, new Vector3(1f, 1f, 1f)),
            center + rotation * Vector3.Scale(corner, new Vector3(1f, 1f, -1f)),
            color, lifetime, type);
        Line(center + rotation * Vector3.Scale(corner, new Vector3(1f, 1f, -1f)),
            center + rotation * Vector3.Scale(corner, new Vector3(-1f, 1f, -1f)),
            color, lifetime, type);
        Line(center + rotation * Vector3.Scale(corner, new Vector3(-1f, 1f, -1f)),
            center + rotation * Vector3.Scale(corner, new Vector3(-1f, 1f, 1f)),
            color, lifetime, type);
        // Bottom
        Line(center + rotation * Vector3.Scale(corner, new Vector3(-1f, -1f, 1f)),
            center + rotation * Vector3.Scale(corner, new Vector3(1f, -1f, 1f)),
            color, lifetime, type);
        Line(center + rotation * Vector3.Scale(corner, new Vector3(1f, -1f, 1f)),
            center + rotation * Vector3.Scale(corner, new Vector3(1f, -1f, -1f)),
            color, lifetime, type);
        Line(center + rotation * Vector3.Scale(corner, new Vector3(1f, -1f, -1f)),
            center + rotation * Vector3.Scale(corner, new Vector3(-1f, -1f, -1f)),
            color, lifetime, type);
        Line(center + rotation * Vector3.Scale(corner, new Vector3(-1f, -1f, -1f)),
            center + rotation * Vector3.Scale(corner, new Vector3(-1f, -1f, 1f)),
            color, lifetime, type);
        // Connectors
        Line(center + rotation * Vector3.Scale(corner, new Vector3(1f, -1f, 1f)),
            center + rotation * Vector3.Scale(corner, new Vector3(1f, 1f, 1f)),
            color, lifetime, type);
        Line(center + rotation * Vector3.Scale(corner, new Vector3(-1f, -1f, 1f)),
            center + rotation * Vector3.Scale(corner, new Vector3(-1f, 1f, 1f)),
            color, lifetime, type);
        Line(center + rotation * Vector3.Scale(corner, new Vector3(-1f, -1f, -1f)),
            center + rotation * Vector3.Scale(corner, new Vector3(-1f, 1f, -1f)),
            color, lifetime, type);
        Line(center + rotation * Vector3.Scale(corner, new Vector3(1f, -1f, -1f)),
            center + rotation * Vector3.Scale(corner, new Vector3(1f, 1f, -1f)),
            color, lifetime, type);
    }

    private IEnumerator Clear()
    {
        yield return wait_for_end_of_frame;
        lines.EndFrame();
        lines_xray.EndFrame();
        spheres.EndFrame();
        spheres_xray.EndFrame();
    }

    public class DebugDrawLine
    {
        public Lifetime lifetime;
        public Vector3 start, end;
        public Color start_color, end_color;
    }

    public class DebugDrawWireSphere
    {
        public Vector3 center, scale;
        public Color color;
        public Lifetime lifetime;
        public Quaternion rotation;
    }
}