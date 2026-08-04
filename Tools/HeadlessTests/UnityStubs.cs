// Hand-written stubs of the Unity API surface this project touches, used only to type-check
// the game scripts on a machine without Unity installed. Not shipped with the project.
using System;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float b) => new Vector2(a.x * b, a.y * b);
        public static Vector2 ClampMagnitude(Vector2 v, float m) => v;
    }

    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new Vector2Int(a.x + b.x, a.y + b.y);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2Int a, Vector2Int b) => !(a == b);
        public bool Equals(Vector2Int other) => this == other;
        public override bool Equals(object o) => o is Vector2Int v && Equals(v);
        public override int GetHashCode() => x * 397 ^ y;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized => this;
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 back => new Vector3(0, 0, -1);
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator *(float b, Vector3 a) => a * b;
    }

    public struct Quaternion
    {
        public static Quaternion identity => default;
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) => default;
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => default;
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public Vector3 center, size;
        public Vector3 min => center - size * 0.5f;
        public Vector3 max => center + size * 0.5f;
    }

    public static class Mathf
    {
        public static float Abs(float v) => Math.Abs(v);
        public static int Abs(int v) => Math.Abs(v);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Exp(float v) => (float)Math.Exp(v);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static float Clamp(float v, float lo, float hi) => Math.Min(hi, Math.Max(lo, v));
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => 0f;
        public static float MoveTowards(float a, float b, float d) => b;
        public static bool Approximately(float a, float b) => a == b;
    }

    public struct Ray
    {
        public Vector3 GetPoint(float d) => default;
    }

    public struct Plane
    {
        public Plane(Vector3 normal, Vector3 point) { }
        public Plane(Vector3 normal, float d) { }
        public bool Raycast(Ray ray, out float distance) { distance = 0f; return true; }
    }

    public enum PrimitiveType { Cube, Capsule, Sphere, Quad, Plane }
    public enum CameraClearFlags { SolidColor, Skybox }
    public enum LightType { Directional, Point, Spot }
    public enum LightShadows { None, Hard, Soft }
    public enum TextAnchor { UpperLeft }
    public enum RenderMode { ScreenSpaceOverlay }

    public class Object
    {
        public string name;
        public static void Destroy(Object o) { if (o is Component c && c.gameObject != null) c.gameObject.Remove(c); }
        public override string ToString() => name ?? GetType().Name;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform;
        public T GetComponent<T>() where T : Component => gameObject == null ? null : gameObject.GetComponent<T>();
    }

    public class Behaviour : Component { public bool enabled = true; }

    public class MonoBehaviour : Behaviour { }

    public class Transform : Component
    {
        public Vector3 position, localPosition, localScale = new Vector3(1, 1, 1);
        public Vector3 forward = new Vector3(0, 0, 1), right = new Vector3(1, 0, 0);
        public Quaternion rotation, localRotation;
        public Transform parent;
        public void SetParent(Transform p, bool worldPositionStays) { parent = p; }
    }

    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta;
    }

    public class GameObject : Object
    {
        readonly System.Collections.Generic.List<Component> _components = new System.Collections.Generic.List<Component>();
        public bool activeSelf = true;
        public string tag;

        public GameObject() : this("GameObject") { }

        public GameObject(string name)
        {
            this.name = name;
            transform = Attach(new Transform());
        }

        public GameObject(string name, params Type[] components) : this(name)
        {
            foreach (var t in components) AddComponent(t);
        }

        public Transform transform { get; private set; }

        public void SetActive(bool value) { activeSelf = value; }

        internal void Remove(Component c) { _components.Remove(c); }

        T Attach<T>(T component) where T : Component
        {
            component.gameObject = this;
            component.transform = component as Transform ?? transform;
            _components.Add(component);
            var awake = component.GetType().GetMethod("Awake",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            awake?.Invoke(component, null);
            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            foreach (var c in _components) if (c is T typed) return typed;
            return null;
        }

        public T AddComponent<T>() where T : Component => (T)AddComponent(typeof(T));

        public Component AddComponent(Type t)
        {
            var component = (Component)Activator.CreateInstance(t);
            return Attach(component);
        }

        public static GameObject CreatePrimitive(PrimitiveType type)
        {
            var go = new GameObject(type.ToString());
            go.AddComponent<Collider>();
            go.AddComponent<MeshRenderer>();
            return go;
        }
    }

    public class Collider : Component { }
    public class Camera : Component
    {
        public CameraClearFlags clearFlags;
        public Color backgroundColor;
        public float nearClipPlane, farClipPlane, fieldOfView;
        public Ray ScreenPointToRay(Vector3 point) => default;
    }
    public class Light : Component
    {
        public LightType type;
        public Color color;
        public float intensity;
        public LightShadows shadows;
    }

    public class Shader : Object { public static Shader Find(string n) => null; public static int PropertyToID(string n) => 0; }

    public class Material : Object
    {
        public Material(Shader s) { }
        public bool HasProperty(string n) => true;
        public bool HasProperty(int id) => true;
        public void SetColor(string n, Color c) { }
        public void SetColor(int id, Color c) { }
        public void SetFloat(string n, float v) { }
    }

    public class MaterialPropertyBlock { public void SetColor(int id, Color c) { } }

    public class Renderer : Component
    {
        public Material sharedMaterial;
        public void GetPropertyBlock(MaterialPropertyBlock b) { }
        public void SetPropertyBlock(MaterialPropertyBlock b) { }
    }

    public class MeshRenderer : Renderer { }
    public class Font : Object { }
    public class ScriptableObject : Object { public static ScriptableObject CreateInstance(Type t) => null; }

    public static class Resources
    {
        public static T GetBuiltinResource<T>(string path) where T : Object => null;
    }

    public static class Input
    {
        public static Vector3 mousePosition => default;
        public static Vector2 mouseScrollDelta => default;
        public static bool GetMouseButtonDown(int b) => false;
        public static bool GetMouseButtonUp(int b) => false;
        public static bool GetMouseButton(int b) => false;
        public static float GetAxisRaw(string axis) => 0f;
    }

    public static class Time { public static float deltaTime = 1f / 60f; }
    public static class Screen { public static int width => 0; public static int height => 0; }
    public static class Application { public static bool isFocused => true; public static bool isBatchMode => false; }
    public static class Debug
    {
        public static void Log(object m) { }
        public static void LogWarning(object m) { }
        public static void LogError(object m) { }
    }

    public static class RenderSettings
    {
        public static Rendering.AmbientMode ambientMode;
        public static Color ambientLight;
    }

    public static class QualitySettings { public static Rendering.RenderPipelineAsset renderPipeline; }

    [AttributeUsage(AttributeTargets.Field)] public class HeaderAttribute : Attribute { public HeaderAttribute(string h) { } }
    [AttributeUsage(AttributeTargets.Field)] public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.Class)] public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int o) { } }
}

namespace UnityEngine.Rendering
{
    using UnityEngine;
    public enum AmbientMode { Skybox, Trilight, Flat, Custom }
    public class RenderPipelineAsset : ScriptableObject { }
    public static class GraphicsSettings
    {
        public static RenderPipelineAsset currentRenderPipeline;
        public static RenderPipelineAsset defaultRenderPipeline;
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public bool IsValid() => true;
        public bool isLoaded => true;
        public int rootCount => 0;
        public GameObject[] GetRootGameObjects() => new GameObject[0];
    }
    public static class SceneManager
    {
        public static Scene GetSceneByPath(string path) => default;
        public static void MoveGameObjectToScene(GameObject go, Scene scene) { }
    }
}

namespace UnityEngine.UI
{
    public class Graphic : Component { public Color color; }
    public class Text : Graphic
    {
        public Font font;
        public int fontSize;
        public string text;
        public TextAnchor alignment;
    }
    public class Canvas : Component { public RenderMode renderMode; }
    public class CanvasScaler : Component
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize }
        public ScaleMode uiScaleMode;
        public Vector2 referenceResolution;
        public float matchWidthOrHeight;
    }
}

namespace TMPro
{
    using UnityEngine;
    public enum TextAlignmentOptions { TopLeft, Center }
    public class TMP_FontAsset : ScriptableObject { }
    public static class TMP_Settings { public static TMP_FontAsset defaultFontAsset => null; }
    public class TextMeshProUGUI : UnityEngine.UI.Graphic
    {
        public float fontSize;
        public string text;
        public TextAlignmentOptions alignment;
    }
}
