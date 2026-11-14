// Minimal Unity API stubs for a semantic compile check only (signatures from the Unity scripting API).
using System;
namespace UnityEngine
{
    public class Object { public string name; public static void Destroy(Object o) {} public static T Instantiate<T>(T o, Transform p, bool w) where T : Object => o; public static T[] FindObjectsByType<T>(FindObjectsSortMode m) where T : Object => null; public static T FindAnyObjectByType<T>() where T : Object => null; public static implicit operator bool(Object o) => o != null; }
    public class Component : Object { public GameObject gameObject; public Transform transform; public T GetComponent<T>() => default; public T GetComponentInParent<T>() => default; public T[] GetComponentsInChildren<T>() => null; public string tag; }
    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object { public GameObject() {} public GameObject(string n) {} public Transform transform; public string tag; public T AddComponent<T>() where T : Component => default; public T GetComponent<T>() => default; public void SetActive(bool b) {} public bool activeSelf; public bool activeInHierarchy; public static GameObject CreatePrimitive(PrimitiveType t) => null; public T[] GetComponentsInChildren<T>() => null; }
    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }
    public class Transform : Component, System.Collections.IEnumerable { public Vector3 TransformDirection(Vector3 v) => v; public Vector3 lossyScale; public Vector3 position, localPosition, localScale, forward, right, eulerAngles, localEulerAngles; public Quaternion rotation, localRotation; public void SetParent(Transform p, bool w) {} public void SetParent(Transform p) {} public void Rotate(float x, float y, float z) {} public bool IsChildOf(Transform t) => false; public Vector3 TransformPoint(Vector3 v) => v; public Vector3 InverseTransformPoint(Vector3 v) => v; public Vector3 InverseTransformDirection(Vector3 v) => v; public Vector3 up; public System.Collections.IEnumerator GetEnumerator() => null; public Transform Find(string n) => null; public Matrix4x4 worldToLocalMatrix; public void SetPositionAndRotation(Vector3 p, Quaternion q) {} }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero = new Vector3(0,0,0), one = new Vector3(1,1,1), up = new Vector3(0,1,0), down = new Vector3(0,-1,0), forward = new Vector3(0,0,1), back = new Vector3(0,0,-1), left = new Vector3(-1,0,0), right = new Vector3(1,0,0);
        public static float Dot(Vector3 a, Vector3 b) => a.x*b.x + a.y*b.y + a.z*b.z; public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y*b.z - a.z*b.y, a.z*b.x - a.x*b.z, a.x*b.y - a.y*b.x);
        public void Normalize() { this = normalized; } public float sqrMagnitude => x*x + y*y + z*z; public float magnitude => (float)Math.Sqrt((double)x*x + (double)y*y + (double)z*z);
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? new Vector3(x/m, y/m, z/m) : new Vector3(0,0,0); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x+b.x, a.y+b.y, a.z+b.z); public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x, a.y-b.y, a.z-b.z); public static Vector3 operator -(Vector3 a) => new Vector3(-a.x,-a.y,-a.z); public static Vector3 operator *(Vector3 a, float f) => new Vector3(a.x*f, a.y*f, a.z*f); public static Vector3 operator *(float f, Vector3 a) => new Vector3(a.x*f, a.y*f, a.z*f); public static Vector3 operator /(Vector3 a, float f) => new Vector3(a.x/f, a.y/f, a.z/f);
        public static float Distance(Vector3 a, Vector3 b) => (a-b).magnitude; public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return a + (b-a)*t; } }
    public struct Vector2 { public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y; public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y); public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } public float magnitude => (float)Math.Sqrt(x*x+y*y); public Vector2 normalized { get { float m = magnitude; return m > 1e-5f ? new Vector2(x/m, y/m) : new Vector2(0,0); } } public float sqrMagnitude => x*x+y*y; public static Vector2 operator *(Vector2 a, float f) => new Vector2(a.x*f,a.y*f); public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x-b.x,a.y-b.y); public static Vector2 operator /(Vector2 a, float f) => new Vector2(a.x/f,a.y/f); public static Vector2 zero = new Vector2(0,0), one = new Vector2(1,1); public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x+b.x,a.y+b.y); public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { t = Mathf.Clamp01(t); return a + (b-a)*t; } }
    public struct Pose { }
    public struct Quaternion { public float x, y, z, w; public Quaternion(float x, float y, float z, float w) { this.x=x; this.y=y; this.z=z; this.w=w; } public static Quaternion identity = new Quaternion(0,0,0,1); public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => t < 0.5f ? a : b;
        public static Quaternion AngleAxis(float deg, Vector3 ax) { ax = ax.normalized; float h = deg * Mathf.Deg2Rad * 0.5f, s = Mathf.Sin(h); return new Quaternion(ax.x*s, ax.y*s, ax.z*s, Mathf.Cos(h)); }
        public static Quaternion Euler(float ex, float ey, float ez) => AngleAxis(ey, Vector3.up) * AngleAxis(ex, Vector3.right) * AngleAxis(ez, Vector3.forward);
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(a.w*b.x + a.x*b.w + a.y*b.z - a.z*b.y, a.w*b.y + a.y*b.w + a.z*b.x - a.x*b.z, a.w*b.z + a.z*b.w + a.x*b.y - a.y*b.x, a.w*b.w - a.x*b.x - a.y*b.y - a.z*b.z);
        public static Quaternion Inverse(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);
        public static Quaternion LookRotation(Vector3 f, Vector3 u) { f = f.normalized; var r = Vector3.Cross(u, f).normalized; u = Vector3.Cross(f, r);
            float m00=r.x,m01=u.x,m02=f.x,m10=r.y,m11=u.y,m12=f.y,m20=r.z,m21=u.z,m22=f.z, tr=m00+m11+m22; Quaternion q;
            if (tr > 0) { float s = Mathf.Sqrt(tr+1f)*2f; q = new Quaternion((m21-m12)/s,(m02-m20)/s,(m10-m01)/s,0.25f*s); }
            else if (m00 > m11 && m00 > m22) { float s = Mathf.Sqrt(1f+m00-m11-m22)*2f; q = new Quaternion(0.25f*s,(m01+m10)/s,(m02+m20)/s,(m21-m12)/s); }
            else if (m11 > m22) { float s = Mathf.Sqrt(1f+m11-m00-m22)*2f; q = new Quaternion((m01+m10)/s,0.25f*s,(m12+m21)/s,(m02-m20)/s); }
            else { float s = Mathf.Sqrt(1f+m22-m00-m11)*2f; q = new Quaternion((m02+m20)/s,(m12+m21)/s,0.25f*s,(m10-m01)/s); }
            return q; }
        public static Quaternion LookRotation(Vector3 f) => LookRotation(f, Vector3.up); public static Quaternion FromToRotation(Vector3 a, Vector3 b) => identity;
        public static Vector3 operator *(Quaternion q, Vector3 v) { var u = new Vector3(q.x,q.y,q.z); var t = Vector3.Cross(u, v) * 2f; return v + t * q.w + Vector3.Cross(u, t); } }
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; } public static Color white, black; public static Color Lerp(Color a, Color b, float t) => a; public static Color operator *(Color c, float f) => c; public static Color operator *(Color c, Color d) => c; public Color linear => this; }
    public struct Rect { public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; xMin = x; yMin = y; xMax = x + w; yMax = y + h; width = w; height = h; center = default; } public float x, y, xMin, yMin, xMax, yMax, width, height; public Vector2 center; public bool Contains(Vector2 p) => false; }
    public static class Mathf { public static float LerpAngle(float a, float b, float t) => a + (b-a)*Clamp01(t); public const float PI = 3.14159265f; public static float Exp(float x) => (float)Math.Exp(x); public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v; public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v; public static float Max(float a, float b) => a > b ? a : b; public static float Min(float a, float b) => a < b ? a : b; public static float Sin(float a) => (float)Math.Sin(a); public static float Sqrt(float a) => (float)Math.Sqrt(a); public static float Sign(float a) => a >= 0 ? 1f : -1f; public static int RoundToInt(float a) => (int)Math.Round(a); public static int CeilToInt(float a) => (int)Math.Ceiling(a); public static float Lerp(float a, float b, float t) => a + (b-a)*Clamp01(t);
        static int[] _p; static float Fade(float t) => t*t*t*(t*(t*6-15)+10); static float Grad(int h, float x, float y) { h &= 7; float u = h < 4 ? x : y, v = h < 4 ? y : x; return ((h&1) != 0 ? -u : u) + ((h&2) != 0 ? -2f*v : 2f*v) * 0.5f; }
        public static float PerlinNoise(float x, float y) { if (_p == null) { _p = new int[512]; var r = new System.Random(1234); var a = new int[256]; for (int i = 0; i < 256; i++) a[i] = i; for (int i = 255; i > 0; i--) { int j = r.Next(i+1); int t = a[i]; a[i] = a[j]; a[j] = t; } for (int i = 0; i < 512; i++) _p[i] = a[i & 255]; }
            int X = (int)Math.Floor(x) & 255, Y = (int)Math.Floor(y) & 255; x -= (float)Math.Floor(x); y -= (float)Math.Floor(y); float u = Fade(x), v = Fade(y); int A = _p[X] + Y, B = _p[X+1] + Y;
            float n = Lerp(Lerp(Grad(_p[A], x, y), Grad(_p[B], x-1, y), u), Lerp(Grad(_p[A+1], x, y-1), Grad(_p[B+1], x-1, y-1), u), v); return Clamp01(n * 0.5f + 0.5f); }
        public static float Abs(float a) => Math.Abs(a); public static int Abs(int a) => Math.Abs(a); public static int Max(int a, int b) => Math.Max(a,b); public static int Min(int a, int b) => Math.Min(a,b); public static float SmoothStep(float a, float b, float t) { t = Clamp01(t); t = -2f*t*t*t + 3f*t*t; return b*t + a*(1f-t); } public static int FloorToInt(float a) => (int)Math.Floor(a); public static float Round(float a) => (float)Math.Round(a); public const float Deg2Rad = 0.01745329252f, Rad2Deg = 57.29577951f; public static float Atan2(float y, float x) => (float)Math.Atan2(y,x); public static float Asin(float a) => (float)Math.Asin(a); public static float Acos(float a) => (float)Math.Acos(a); public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v; public static float InverseLerp(float a, float b, float v) => a == b ? 0 : Clamp01((v-a)/(b-a)); public static float Repeat(float t, float l) => t - (float)Math.Floor(t/l)*l; public static float Cos(float a) => (float)Math.Cos(a); public static float Pow(float a, float b) => (float)Math.Pow(a,b); public static float MoveTowards(float a, float b, float d) => Math.Abs(b-a) <= d ? b : a + Math.Sign(b-a)*d; }
    public static class Time { public static float deltaTime, unscaledDeltaTime, unscaledTime, time, timeScale; }
    public struct RaycastHit { public float distance; public Vector3 point; public Collider collider; }
    public struct Ray { public Ray(Vector3 o, Vector3 d) {} }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }
    public static class Physics { public static Vector3 gravity; public static RaycastHit[] SphereCastAll(Vector3 o, float r, Vector3 d, float m, int mask, QueryTriggerInteraction q) => null; public static void SyncTransforms() {} public static RaycastHit[] RaycastAll(Ray r, float d, int mask, QueryTriggerInteraction q) => null; public static bool Raycast(Vector3 o, Vector3 d, out RaycastHit h, float m, int mask, QueryTriggerInteraction q) { h = default; return false; } }
    public class Collider : Component { public bool enabled, isTrigger; }
    public class BoxCollider : Collider { public Vector3 size, center; }
    public class CharacterController : Collider { public float height, radius, slopeLimit, stepOffset; public Vector3 center; public bool isGrounded; public void Move(Vector3 v) {} }
    public class Camera : Behaviour { public static Camera main; public float nearClipPlane, farClipPlane, fieldOfView; }
    public class Animator : Behaviour { public float speed; public bool applyRootMotion; public static int StringToHash(string s) => 0; public void SetFloat(int id, float v) {} public void SetBool(int id, bool v) {} public void SetIKPositionWeight(AvatarIKGoal g, float w) {} public void SetIKRotationWeight(AvatarIKGoal g, float w) {} public void SetIKPosition(AvatarIKGoal g, Vector3 p) {} public void SetIKRotation(AvatarIKGoal g, Quaternion q) {} public Quaternion GetIKRotation(AvatarIKGoal g) => default; public Vector3 GetIKPosition(AvatarIKGoal g) => default; public void SetIKHintPositionWeight(AvatarIKHint h, float w) {} public void SetIKHintPosition(AvatarIKHint h, Vector3 p) {} public Transform GetBoneTransform(HumanBodyBones b) => null; public bool isHuman; }
    public enum AvatarIKGoal { LeftFoot, RightFoot, LeftHand, RightHand }
    public enum AvatarIKHint { LeftKnee, RightKnee, LeftElbow, RightElbow }
    public enum HumanBodyBones { Hips, Head, Neck, Spine, Chest, UpperChest, LeftUpperArm, RightUpperArm, LeftLowerArm, RightLowerArm, LeftHand, RightHand, LeftUpperLeg, RightUpperLeg, LeftLowerLeg, RightLowerLeg, LeftFoot, RightFoot }
    public class AudioListener : Behaviour { }
    public enum LightType { Directional }
    public enum LightShadows { None, Hard, Soft }
    public class Light : Behaviour { public LightType type; public float intensity, shadowStrength; public Color color; public LightShadows shadows; }
    public enum FogMode { Linear }
    public static class RenderSettings { public static Light sun; public static Material skybox; public static UnityEngine.Rendering.AmbientMode ambientMode; public static Color ambientSkyColor, ambientEquatorColor, ambientGroundColor, fogColor; public static bool fog; public static FogMode fogMode; public static float fogStartDistance, fogEndDistance; }
    public class Texture : Object { public TextureWrapMode wrapMode, wrapModeU; public FilterMode filterMode; }
    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Repeat, Clamp }
    public enum FilterMode { Bilinear }
    public class Texture2D : Texture { public Texture2D(int w, int h, TextureFormat f, bool mip) {} public Texture2D(int w, int h) {} public void SetPixels(Color[] c) {} public void SetPixel(int x, int y, Color c) {} public void Apply(bool m) {} public void Apply() {} }
    public class Font : Object { public Material material; }
    public enum TextAlignment { Center }
    public class TextMesh : Component { public Font font; public int fontSize; public float characterSize; public TextAnchor anchor; public TextAlignment alignment; public FontStyle fontStyle; public Color color; public string text; }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject => null; }
    public class Renderer : Component { public Material sharedMaterial; public bool enabled; public Bounds bounds; public Material[] sharedMaterials; public bool receiveShadows; public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode; }
    public class MeshRenderer : Renderer { }
    public struct Vector2Int { public int x, y; public Vector2Int(int x, int y) { this.x = x; this.y = y; } }
    public class Mesh : Object { public UnityEngine.Rendering.IndexFormat indexFormat; public void SetVertices(System.Collections.Generic.List<Vector3> v) {} public void SetNormals(System.Collections.Generic.List<Vector3> v) {} public void SetUVs(int c, System.Collections.Generic.List<Vector2> v) {} public void SetTriangles(System.Collections.Generic.List<int> t, int s) {} public void RecalculateBounds() {} public void SetColors(System.Collections.Generic.List<Color> c) {} public void CombineMeshes(CombineInstance[] c, bool merge, bool useMatrices) {} }
    public struct CombineInstance { public Mesh mesh; public Matrix4x4 transform; }
    public struct Matrix4x4 { public static Matrix4x4 identity; public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s) => default; public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => a; public Vector3 MultiplyPoint3x4(Vector3 v) => v; public Vector3 MultiplyVector(Vector3 v) => v; }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class MeshCollider : Collider { public Mesh sharedMesh; }
    public struct Bounds { public Vector3 size; }
    public enum FindObjectsSortMode { None }
    public static class Screen { public static int width, height; }
    public class LineRenderer : Renderer { public bool useWorldSpace; public float widthMultiplier; public int positionCount; public void SetPosition(int i, Vector3 p) {} }
    public class Shader : Object { public static Shader Find(string n) => null; public bool isSupported => true; public static void SetGlobalMatrix(int id, Matrix4x4 m) {} public static int PropertyToID(string n) => 0; }
    public class Material : Object { public Material(Shader s) {} public Material(Material m) {} public Texture mainTexture; public void SetTexture(string n, Texture t) {} public void SetTextureScale(string n, Vector2 v) {} public void EnableKeyword(string k) {} public bool HasProperty(string n) => false; public void SetColor(string n, Color c) {} public void SetFloat(string n, float f) {} }
    public enum CursorLockMode { None, Locked }
    public static class Cursor { public static CursorLockMode lockState; public static bool visible; }
    public static class Debug { public static void Log(object o) {} public static void LogWarning(object o) {} public static void LogError(object o) {} }
    public static class Application { public static bool isEditor, runInBackground; public static string dataPath, persistentDataPath; public static void Quit() {} }
    public class TextAsset : Object { public string text; public byte[] bytes; }
    public static class Resources { public static string[] Roots = new string[0];
        static TextAsset Read(string f) { var t = new TextAsset { name = System.IO.Path.GetFileNameWithoutExtension(f), bytes = System.IO.File.ReadAllBytes(f) }; t.text = System.Text.Encoding.UTF8.GetString(t.bytes); return t; }
        public static T Load<T>(string p) where T : Object { if (typeof(T) != typeof(TextAsset)) return null; foreach (var r in Roots) foreach (var e in new[] { ".bytes", ".txt", ".json" }) { var f = System.IO.Path.Combine(r, p + e); if (System.IO.File.Exists(f)) return Read(f) as T; } return null; }
        public static T[] LoadAll<T>(string p) where T : Object { var l = new System.Collections.Generic.List<T>(); if (typeof(T) == typeof(TextAsset)) foreach (var r in Roots) { var d = System.IO.Path.Combine(r, p); if (!System.IO.Directory.Exists(d)) continue; var fs = System.IO.Directory.GetFiles(d); System.Array.Sort(fs, string.CompareOrdinal); foreach (var f in fs) if (f.EndsWith(".bytes") || f.EndsWith(".txt")) l.Add(Read(f) as T); } return l.ToArray(); }
        public static T GetBuiltinResource<T>(string p) where T : Object => null; }
    public static class JsonUtility { public static T FromJson<T>(string s) => default; }
    public static class Random { public static Vector3 insideUnitSphere; }
    public enum FontStyle { Normal, Bold, Italic }
    public enum ColorSpace { Gamma, Linear }
    public static class QualitySettings { public static ColorSpace activeColorSpace; }
    public enum TextAnchor { MiddleCenter }
    public class GUIStyleState { public Color textColor; public Texture2D background; }
    public class GUIStyle { public GUIStyle() {} public GUIStyle(GUIStyle o) {} public int fontSize; public FontStyle fontStyle; public TextAnchor alignment; public GUIStyleState normal = new GUIStyleState(); }
    public class GUISkin { public GUIStyle label, box; }
    public class GUIContent { public static GUIContent none; }
    public static class GUI { public static GUISkin skin; public static Color color; public static void Label(Rect r, string t, GUIStyle s) {} public static void Box(Rect r, GUIContent c, GUIStyle s) {} public static void Box(Rect r, GUIContent c) {} public static void Box(Rect r, string t, GUIStyle s) {} public static bool Button(Rect r, string t) => false; public static void DrawTexture(Rect r, Texture t) {} public static void DrawTextureWithTexCoords(Rect r, Texture t, Rect uv) {} public static Matrix4x4 matrix; }
    public static class GUIUtility { public static void RotateAroundPivot(float a, Vector2 p) {} }
    public enum RuntimeInitializeLoadType { AfterSceneLoad }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) {} }
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int o) {} }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public enum LoadSceneMode { Single }
    public static class SceneManager { public static Scene GetActiveScene() => default; public static void LoadScene(string n) {} public static event Action<Scene, LoadSceneMode> sceneLoaded; }
}
namespace UnityEngine.InputSystem.Controls
{
    public class ButtonControl { public bool isPressed, wasPressedThisFrame; }
    public class KeyControl : ButtonControl { }
    public class Vector2Control { public UnityEngine.Vector2 ReadValue() => default; }
}
namespace UnityEngine.InputSystem
{
    using UnityEngine.InputSystem.Controls;
    public class Keyboard { public static Keyboard current; public KeyControl tKey, shiftKey, wKey, vKey, aKey, sKey, dKey, eKey, gKey, cKey, spaceKey, escapeKey, leftShiftKey, rightShiftKey, leftCtrlKey, digit1Key, digit2Key, mKey, equalsKey, numpadPlusKey, minusKey, numpadMinusKey; }
    public class Mouse { public static Mouse current; public ButtonControl leftButton; public Vector2Control delta, scroll; }
}
namespace UnityEditor { public static class EditorApplication { public static bool isPlaying; } }

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
    public enum IndexFormat { UInt16, UInt32 }
    public enum AmbientMode { Trilight }
    public class VolumeParameter<T> { public void Override(T v) {} }
    public class VolumeComponent : ScriptableObject { }
    public class VolumeProfile : ScriptableObject { public T Add<T>(bool overrides = false) where T : VolumeComponent => null; }
    public class Volume : MonoBehaviour { public bool isGlobal; public float priority; public VolumeProfile sharedProfile; }
}
namespace UnityEngine.Rendering.Universal
{
    public enum TonemappingMode { None, Neutral, ACES }
    public enum AntialiasingMode { SubpixelMorphologicalAntiAliasing }
    public class Tonemapping : VolumeComponent { public VolumeParameter<TonemappingMode> mode; }
    public class ColorAdjustments : VolumeComponent { public VolumeParameter<float> postExposure, contrast, saturation; public VolumeParameter<Color> colorFilter; }
    public class Bloom : VolumeComponent { public VolumeParameter<float> intensity, threshold, scatter; }
    public class SplitToning : VolumeComponent { public VolumeParameter<Color> shadows, highlights; public VolumeParameter<float> balance; }
    public class Vignette : VolumeComponent { public VolumeParameter<float> intensity, smoothness; }
    public class UniversalAdditionalCameraData : MonoBehaviour { public bool renderPostProcessing, stopNaN; public AntialiasingMode antialiasing; }
    public class UniversalRenderPipelineAsset { public float shadowDistance; public int shadowCascadeCount; }
    public static class UniversalRenderPipeline { public static UniversalRenderPipelineAsset asset; }
    public static class CameraExtensions { public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera c) => null; }
}
