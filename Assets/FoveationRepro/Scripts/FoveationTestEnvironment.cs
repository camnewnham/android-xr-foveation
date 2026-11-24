using UnityEngine;

namespace FoveationRepro
{
    /// <summary>
    /// Builds a high-frequency test environment around the user at runtime so the effect of foveation is easy to see.
    /// Fixed foveation lowers the shading rate toward the edges of each eye's view. Fine detail there
    /// (Siemens stars, 1-px lines, small text-like checkers) turns blocky or smeared when foveation is
    /// on, and stays sharp when it is off. Look straight ahead and compare the panels in your
    /// peripheral vision with the one in the centre. Then turn your head so a peripheral panel is in
    /// the centre and compare again.
    /// </summary>
    public class FoveationTestEnvironment : MonoBehaviour
    {
        [Tooltip("Centre of the ring. Defaults to the main camera's start position.")]
        public Transform center;
        public int panelCount = 16;
        public float radius = 2.2f;
        public float panelHeight = 2.4f;
        public int textureSize = 1024;
        public bool buildFloor = true;
        [Tooltip("URP Unlit material used as a template. Referenced from the scene so the shader is included in builds.")]
        public Material unlitMaterial;

        void Start()
        {
            Vector3 c = center != null ? center.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            float floorY = transform.position.y;

            var panelTex = BuildPanelTexture(textureSize);
            var panelMat = NewMat("FoveationTestPanel");
            panelMat.SetTexture("_BaseMap", panelTex);
            panelMat.SetColor("_BaseColor", Color.white);

            float circumference = 2 * Mathf.PI * radius;
            float panelWidth = circumference / panelCount * 1.02f;
            for (int i = 0; i < panelCount; i++)
            {
                float a = i * Mathf.PI * 2 / panelCount;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = $"Panel {i:00}";
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(transform, false);
                q.transform.position = new Vector3(c.x, floorY + panelHeight * 0.5f + 0.2f, c.z) + dir * radius;
                q.transform.rotation = Quaternion.LookRotation(dir);
                q.transform.localScale = new Vector3(panelWidth, panelHeight, 1);
                q.GetComponent<Renderer>().sharedMaterial = panelMat;
            }

            if (buildFloor)
            {
                var floorTex = BuildGridTexture(textureSize);
                var floorMat = NewMat("FoveationTestFloor");
                floorMat.SetTexture("_BaseMap", floorTex);
                floorMat.SetTextureScale("_BaseMap", new Vector2(radius * 2, radius * 2));
                var f = GameObject.CreatePrimitive(PrimitiveType.Quad);
                f.name = "Floor Grid";
                Destroy(f.GetComponent<Collider>());
                f.transform.SetParent(transform, false);
                f.transform.position = new Vector3(c.x, floorY + 0.01f, c.z);
                f.transform.rotation = Quaternion.Euler(90, 0, 0);
                f.transform.localScale = new Vector3(radius * 2, radius * 2, 1);
                f.GetComponent<Renderer>().sharedMaterial = floorMat;
            }
        }

        Material NewMat(string n)
        {
            var m = unlitMaterial != null ? new Material(unlitMaterial) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.name = n;
            return m;
        }

        /// <summary>A Siemens star over bands of 1, 2 and 4-pixel checkers and fine stripes.</summary>
        static Texture2D BuildPanelTexture(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SiemensStar", anisoLevel = 8, wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[n * n];
            var black = new Color32(10, 10, 10, 255);
            var white = new Color32(245, 245, 245, 255);
            int spokes = 72;
            float cx = n * 0.5f, cy = n * 0.5f, starR = n * 0.36f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool on;
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r < starR)
                {
                    float ang = Mathf.Atan2(dy, dx);
                    on = Mathf.Repeat(ang / (2 * Mathf.PI) * spokes, 1f) < 0.5f;
                }
                else
                {
                    // Corner bands: checker size depends on which quadrant / ring we are in.
                    int cell = y < n / 8 ? 1 : y < n / 4 ? 2 : y > n - n / 8 ? 1 : y > n - n / 4 ? 4 : (x < n / 2 ? 2 : 4);
                    bool stripes = x < n / 10 || x > n - n / 10;
                    on = stripes ? (y / cell) % 2 == 0 : ((x / cell) + (y / cell)) % 2 == 0;
                }
                // Coloured border so panels are easy to tell apart and edges show aliasing.
                if (x < 6 || y < 6 || x >= n - 6 || y >= n - 6)
                    px[y * n + x] = new Color32(255, 60, 60, 255);
                else
                    px[y * n + x] = on ? white : black;
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>1-pixel lines every 16 px with a heavier line every 128 px. Repeats once per metre.</summary>
        static Texture2D BuildGridTexture(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "FloorGrid", anisoLevel = 8, wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool major = x % 128 < 3 || y % 128 < 3;
                bool minor = x % 16 == 0 || y % 16 == 0;
                px[y * n + x] = major ? new Color32(40, 200, 255, 255)
                              : minor ? new Color32(200, 200, 200, 255)
                              : new Color32(20, 20, 25, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
