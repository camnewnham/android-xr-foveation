using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace FoveationRepro
{
    /// <summary>
    /// Requests fixed foveated rendering at the maximum level through the SRP foveation API
    /// (<see cref="XRDisplaySubsystem.foveatedRenderingLevel"/>), as described in the Unity OpenXR docs.
    /// </summary>
    public class FoveationRequester : MonoBehaviour
    {
        [Tooltip("0 = off, 1 = maximum.")]
        [Range(0f, 1f)]
        public float requestedLevel = 1f;

        [Tooltip("Fixed foveation only: leave GazeAllowed off.")]
        public bool allowGaze = false;

        [Tooltip("Set the level every frame, in case the runtime or URP resets it after startup.")]
        public bool reapplyEveryFrame = true;

        public XRDisplaySubsystem Display { get; private set; }
        public bool HasApplied { get; private set; }
        public int ApplyCount { get; private set; }

        static readonly List<XRDisplaySubsystem> s_Displays = new List<XRDisplaySubsystem>();

        void Update()
        {
            if (Display == null || !Display.running)
            {
                SubsystemManager.GetSubsystems(s_Displays);
                Display = null;
                foreach (var d in s_Displays)
                {
                    if (d.running) { Display = d; break; }
                }
                if (Display == null) return;
            }

            if (!HasApplied || reapplyEveryFrame)
                Apply();
        }

        public void Apply()
        {
            if (Display == null) return;

            Display.foveatedRenderingFlags = allowGaze
                ? XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed
                : XRDisplaySubsystem.FoveatedRenderingFlags.None;
            Display.foveatedRenderingLevel = requestedLevel;

            if (!HasApplied)
                Debug.Log($"[FoveationRepro] Requested foveatedRenderingLevel={requestedLevel}, flags={Display.foveatedRenderingFlags}. " +
                          $"Read back level={Display.foveatedRenderingLevel}, caps={SystemInfo.foveatedRenderingCaps}");

            HasApplied = true;
            ApplyCount++;
        }
    }
}
