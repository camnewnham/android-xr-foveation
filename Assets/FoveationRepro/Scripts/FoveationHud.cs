using System;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.NativeTypes;
using UnityEngine.XR.Management;

namespace FoveationRepro
{
    /// <summary>
    /// Floating panel that shows what <see cref="FoveationRequester"/> asked for and what OpenXR / Unity report back.
    /// Builds its own TextMeshPro text and backing quad at runtime and lazily follows the head.
    /// The same report goes to logcat (tag Unity, prefix [FoveationRepro]) every few seconds.
    /// </summary>
    public class FoveationHud : MonoBehaviour
    {
        public FoveationRequester controller;
        public Transform head;
        [Tooltip("URP Unlit material for the panel background. Referenced from the scene so the shader is included in builds.")]
        public Material unlitMaterial;

        [Header("Placement")]
        public float distance = 1.4f;
        public float verticalOffset = -0.2f;
        public float followSpeed = 2f;
        public Vector2 panelSize = new Vector2(0.9f, 1.05f);

        [Header("Refresh")]
        public float refreshInterval = 0.5f;
        public float logInterval = 5f;

        static readonly string[] k_Extensions =
        {
            "XR_FB_foveation",
            "XR_FB_foveation_configuration",
            "XR_FB_foveation_vulkan",
            "XR_FB_swapchain_update_state",
            "XR_META_foveation_eye_tracked",
            "XR_META_vulkan_swapchain_create_info",
        };

        // System setting / property the Android XR runtime is believed to check.
        const string k_DisableFoveationKey = "xr.disable_foveation";

        TextMeshPro m_Text;
        float m_NextRefresh;
        float m_NextLog;
        string m_AndroidSettingCache;
        float m_NextAndroidRead;

        static MethodInfo s_GetFbLevel;
        static bool s_LookedUpFbLevel;

        void Start()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (controller == null) controller = FindAnyObjectByType<FoveationRequester>();
            BuildVisuals();
            SnapToHead();
        }

        void BuildVisuals()
        {
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "HUD Background";
            Destroy(bg.GetComponent<Collider>());
            bg.transform.SetParent(transform, false);
            bg.transform.localPosition = new Vector3(0, 0, 0.005f);
            bg.transform.localScale = new Vector3(panelSize.x, panelSize.y, 1);
            var mat = unlitMaterial != null ? new Material(unlitMaterial) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.08f, 1f));
            bg.GetComponent<Renderer>().sharedMaterial = mat;

            var textGo = new GameObject("HUD Text");
            textGo.transform.SetParent(transform, false);
            m_Text = textGo.AddComponent<TextMeshPro>();
            m_Text.rectTransform.sizeDelta = panelSize - new Vector2(0.06f, 0.06f);
            m_Text.fontSize = 0.224f;
            m_Text.alignment = TextAlignmentOptions.TopLeft;
            m_Text.textWrappingMode = TextWrappingModes.Normal;
            m_Text.richText = true;
            m_Text.color = Color.white;
        }

        void SnapToHead()
        {
            if (head == null) return;
            transform.SetPositionAndRotation(TargetPosition(), TargetRotation());
        }

        Vector3 TargetPosition()
        {
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            return head.position + fwd.normalized * distance + Vector3.up * verticalOffset;
        }

        Quaternion TargetRotation()
        {
            var toPanel = transform.position - head.position;
            toPanel.y = 0;
            return toPanel.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toPanel) : transform.rotation;
        }

        void LateUpdate()
        {
            if (head != null)
            {
                float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, TargetPosition(), t);
                transform.rotation = Quaternion.Slerp(transform.rotation, TargetRotation(), t);
            }

            if (Time.unscaledTime < m_NextRefresh) return;
            m_NextRefresh = Time.unscaledTime + refreshInterval;

            var report = BuildReport(out var plain);
            if (m_Text != null) m_Text.text = report;

            if (Time.unscaledTime >= m_NextLog)
            {
                m_NextLog = Time.unscaledTime + logInterval;
                Debug.Log("[FoveationRepro]\n" + plain);
            }
        }

        string BuildReport(out string plain)
        {
            var sb = new StringBuilder();
            var problems = new StringBuilder();

            sb.AppendLine("<b>Android XR foveation repro</b>");
            sb.AppendLine();

            // --- What we asked for
            sb.AppendLine("<b><color=#8cf>Requested</color></b>");
            if (controller != null)
            {
                sb.AppendLine($"  foveatedRenderingLevel = {controller.requestedLevel:0.00}");
                sb.AppendLine($"  flags = {(controller.allowGaze ? "GazeAllowed" : "None (fixed)")}");
                sb.AppendLine($"  applied = {controller.HasApplied} (x{controller.ApplyCount})");
            }
            else
            {
                sb.AppendLine("  <color=#f66>No FoveationRequester in scene</color>");
                problems.AppendLine("no controller");
            }
            sb.AppendLine();

            // --- What Unity / OpenXR report back
            sb.AppendLine("<b><color=#8cf>Read back</color></b>");

            var feature = OpenXRSettings.Instance != null ? OpenXRSettings.Instance.GetFeature<FoveatedRenderingFeature>() : null;
            bool featureOn = feature != null && feature.enabled;
            sb.AppendLine($"  FoveatedRenderingFeature: {Flag(featureOn)}");
            if (!featureOn) problems.AppendLine("feature disabled");

            var api = OpenXRSettings.Instance != null ? OpenXRSettings.Instance.foveatedRenderingApi.ToString() : "?";
            sb.AppendLine($"  Method: {api}");

            var caps = SystemInfo.foveatedRenderingCaps;
            sb.AppendLine($"  SystemInfo.foveatedRenderingCaps: {Col(caps.ToString(), caps != FoveatedRenderingCaps.None)}");
            if (caps == FoveatedRenderingCaps.None) problems.AppendLine("caps = None");

            var display = controller != null ? controller.Display : null;
            if (display != null)
            {
                float level = display.foveatedRenderingLevel;
                bool levelOk = controller == null || Mathf.Approximately(level, controller.requestedLevel);
                sb.AppendLine($"  display.foveatedRenderingLevel: {Col(level.ToString("0.00"), levelOk && level > 0)}");
                sb.AppendLine($"  display.foveatedRenderingFlags: {display.foveatedRenderingFlags}");
                if (level <= 0) problems.AppendLine("display level = 0");
            }
            else
            {
                sb.AppendLine("  <color=#fa4>XRDisplaySubsystem not running yet</color>");
                problems.AppendLine("no display subsystem");
            }

            var fb = TryGetFbLevel(out var fbLevel, out var fbResult);
            if (fb)
            {
                sb.AppendLine($"  Runtime FB level (xrGet): {Col(fbLevel.ToString(), fbLevel != XrFoveationLevelFB.None)}");
                if (fbLevel == XrFoveationLevelFB.None) problems.AppendLine("runtime level = None");
            }
            else
            {
                sb.AppendLine($"  Runtime FB level (xrGet): <color=#fa4>{fbResult}</color>");
            }

            if (feature != null && OpenXRActive)
            {
                try { sb.AppendLine($"  Dynamic foveation: {feature.DynamicFoveationEnabled}"); }
                catch (Exception e) { sb.AppendLine($"  Dynamic foveation: {e.GetType().Name}"); }
            }
            sb.AppendLine();

            // --- Extensions
            sb.AppendLine("<b><color=#8cf>OpenXR extensions enabled</color></b>");
            foreach (var ext in k_Extensions)
            {
                bool on = OpenXRRuntime.IsExtensionEnabled(ext);
                sb.AppendLine($"  {Flag(on)} {ext}");
                if (!on && ext == "XR_FB_foveation") problems.AppendLine("XR_FB_foveation not enabled");
            }
            sb.AppendLine();

            // --- Environment
            sb.AppendLine("<b><color=#8cf>Environment</color></b>");
            sb.AppendLine($"  Runtime: {OpenXRRuntime.name} {OpenXRRuntime.version} (API {OpenXRRuntime.apiVersion})");
            sb.AppendLine($"  OpenXR plugin {OpenXRRuntime.pluginVersion}, Unity {Application.unityVersion}");
            sb.AppendLine($"  GFX: {SystemInfo.graphicsDeviceType}, RP: {(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "Built-in")}");
            sb.AppendLine($"  {k_DisableFoveationKey}: {ReadAndroidSetting()}");
            sb.AppendLine();

            // --- Verdict
            if (problems.Length == 0)
                sb.AppendLine("<b><color=#6f6>Verdict: foveation SHOULD be active</color></b>");
            else
                sb.AppendLine("<b><color=#f66>Verdict: foveation NOT active</color></b>\n  " + problems.ToString().TrimEnd().Replace("\n", ", "));

            var rich = sb.ToString();
            plain = System.Text.RegularExpressions.Regex.Replace(rich, "<.*?>", "");
            return rich;
        }

        static bool OpenXRActive
        {
            get
            {
                var m = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
                return m != null && m.activeLoader is OpenXRLoaderBase;
            }
        }

        static string Flag(bool on) => on ? "<color=#6f6>ON</color>" : "<color=#f66>OFF</color>";
        static string Col(string s, bool good) => good ? $"<color=#6f6>{s}</color>" : $"<color=#f66>{s}</color>";

        /// <summary>
        /// Calls the OpenXR package's own native readback (FBGetFoveationLevel, which wraps the runtime's
        /// foveation profile state). It is private in the package, so it is reached by reflection.
        /// </summary>
        static bool TryGetFbLevel(out XrFoveationLevelFB level, out string result)
        {
            level = XrFoveationLevelFB.None;
            if (!s_LookedUpFbLevel)
            {
                s_LookedUpFbLevel = true;
                s_GetFbLevel = typeof(FoveatedRenderingFeature).GetMethod("Internal_GetFbFoveationLevel",
                    BindingFlags.Static | BindingFlags.NonPublic);
            }
            if (s_GetFbLevel == null) { result = "unavailable"; return false; }
            if (!OpenXRActive) { result = "no OpenXR loader"; return false; }
            try
            {
                var args = new object[] { null };
                var xr = (XrResult)s_GetFbLevel.Invoke(null, args);
                if (xr != XrResult.Success) { result = xr.ToString(); return false; }
                level = (XrFoveationLevelFB)args[0];
                result = "Success";
                return true;
            }
            catch (Exception e)
            {
                result = (e.InnerException ?? e).GetType().Name;
                return false;
            }
        }

        string ReadAndroidSetting()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_AndroidSettingCache != null && Time.unscaledTime < m_NextAndroidRead) return m_AndroidSettingCache;
            m_NextAndroidRead = Time.unscaledTime + 5f;
            var parts = new StringBuilder();
            try
            {
                using var props = new AndroidJavaClass("android.os.SystemProperties");
                var v = props.CallStatic<string>("get", k_DisableFoveationKey, "<unset>");
                parts.Append($"prop={v}");
            }
            catch (Exception) { parts.Append("prop=?"); }
            try
            {
                using var activity = UnityEngine.Android.AndroidApplication.currentActivity;
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                foreach (var table in new[] { "Global", "Secure", "System" })
                {
                    using var cls = new AndroidJavaClass("android.provider.Settings$" + table);
                    var v = cls.CallStatic<string>("getString", resolver, k_DisableFoveationKey);
                    parts.Append($" {table.ToLowerInvariant()}={v ?? "<unset>"}");
                }
            }
            catch (Exception) { parts.Append(" settings=?"); }
            m_AndroidSettingCache = parts.ToString();
            return m_AndroidSettingCache;
#else
            return "n/a (editor)";
#endif
        }
    }
}
