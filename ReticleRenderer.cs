using EFT.CameraControl;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Renders the scope reticle as a world-space MeshRenderer GameObject.
    ///
    /// ── WHY WORLD-SPACE GAMEOBJECT, NOT COMMANDBUFFER ───────────────────────
    /// In vanilla Tarkov (PiP mode) the reticle is a texture on the back-lens
    /// MeshRenderer — a regular world-space scene object.  It renders through
    /// the normal scene pipeline, benefits from TAA (which filters float-
    /// precision noise on big maps), and is inherently locked to the housing
    /// because both are in the same transform hierarchy.
    ///
    /// The previous CommandBuffer approach injected draws at AfterEverything,
    /// which runs AFTER TAA.  Every precision-fix attempt (shared world matrix,
    /// EMA smoothing) was fighting the root cause: the reticle never received
    /// TAA's temporal filtering that the housing gets for free.
    ///
    /// The fix: use a regular MeshRenderer, exactly like vanilla.  Position it
    /// every frame in OnPreCull (after animation LateUpdate, before culling),
    /// and let Unity render it as a normal scene object through TAA.
    ///
    /// ── CAMERA ALIGNMENT ────────────────────────────────────────────────────
    /// OnPreCull still overrides cam.rotation = opticCam.rotation so the scene
    /// renders as the scope sees it.  The reticle object is then just another
    /// mesh in that scene, naturally appearing at the back-lens position.
    /// </summary>
    internal static class ReticleRenderer
    {
        private static Material _reticleMat;
        private static Mesh     _reticleMesh;
        private static Texture  _savedMarkTex;
        private static Texture  _savedMaskTex;

        // Scale tracking
        private static float _baseScale;
        private static float _lastMag = 1f;

        // Cached transforms
        private static Transform _opticTransform;
        private static Transform _backLensTransform;
        private static Transform _reticleAnchor;
        private static Renderer  _lensRenderer;

        // World-space reticle rendered as a normal scene object.
        // Renders through the standard pipeline → benefits from TAA → locked to
        // the housing with no manual precision compensation required.
        private static GameObject   _reticleObject;
        private static MeshRenderer _reticleObjectRenderer;

        // Camera state
        private static Camera _alignedCamera;
        private static bool   _preCullRegistered;
        private static bool   _alignmentActive;
        private static bool   _settled;

        // Weapon-scale compensation offset (NDC, -1..1).
        // Only non-zero when WeaponScalingPatch is active; read by ScopeEffectsRenderer.
        private static Vector2 _weaponScaleOffset;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Extract reticle textures from the OpticSight's lens material.
        /// MUST be called BEFORE LensTransparency replaces the mesh.
        /// </summary>
        public static void ExtractReticle(OpticSight os)
        {
            if (!ScopeHousingMeshSurgeryPlugin.ShowReticle.Value) return;
            if (os == null) return;

            _savedMarkTex = null;
            _savedMaskTex = null;

            try
            {
                Renderer lensRenderer = os.LensRenderer;
                if (lensRenderer == null) return;

                Material mat = null;
                foreach (var m in lensRenderer.sharedMaterials)
                {
                    if (m != null && m.shader != null && m.shader.name.Contains("OpticSight"))
                    { mat = m; break; }
                }
                if (mat == null) mat = lensRenderer.sharedMaterial;
                if (mat == null) return;

                if (mat.HasProperty("_MarkTex"))
                    _savedMarkTex = mat.GetTexture("_MarkTex");
                if (mat.HasProperty("_MaskTex"))
                    _savedMaskTex = mat.GetTexture("_MaskTex");

                if (_savedMarkTex != null)
                {
                    _savedMarkTex.filterMode = FilterMode.Trilinear;
                    _savedMarkTex.anisoLevel = 16;
                    if (_savedMarkTex is Texture2D tex2d)
                        tex2d.mipMapBias = ScopeHousingMeshSurgeryPlugin.ReticleMipBias.Value;
                }

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[Reticle] Extracted: _MarkTex={(_savedMarkTex != null ? _savedMarkTex.name : "null")} " +
                    $"({(_savedMarkTex != null ? $"{_savedMarkTex.width}x{_savedMarkTex.height}" : "?")}) " +
                    $"_MaskTex={(_savedMaskTex != null ? _savedMaskTex.name : "null")} " +
                    $"filter=Trilinear aniso=16 mipBias={ScopeHousingMeshSurgeryPlugin.ReticleMipBias.Value}");
            }
            catch (System.Exception e)
            {
                ScopeHousingMeshSurgeryPlugin.LogError($"[Reticle] Extract failed: {e.Message}");
            }
        }

        /// <summary>
        /// Show the reticle.  Creates a world-space MeshRenderer object and
        /// registers the OnPreCull hook for camera alignment.
        /// </summary>
        public static void Show(OpticSight os, float magnification = 1f)
        {
            if (!ScopeHousingMeshSurgeryPlugin.ShowReticle.Value) return;
            if (_savedMarkTex == null || os == null) return;

            try
            {
                _opticTransform = os.transform;

                _backLensTransform = ScopeHierarchy.FindDeepChild(os.transform, "backLens")
                                  ?? ScopeHierarchy.FindDeepChild(os.transform, "backlens");

                _lensRenderer  = os.LensRenderer;
                _reticleAnchor = _lensRenderer != null ? _lensRenderer.transform : null;
                if (_reticleAnchor == null) _reticleAnchor = _opticTransform;

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[Reticle] backLensTransform={(_backLensTransform != null ? _backLensTransform.name : "not found")}; " +
                    $"fallback anchor={(_reticleAnchor != null ? _reticleAnchor.name : "null")}");

                EnsureMeshAndMaterial();
                _reticleMat.mainTexture = _savedMarkTex;
                ApplyHorizontalFlip();

                float configBase = ScopeHousingMeshSurgeryPlugin.ReticleBaseSize.Value;
                _baseScale = configBase > 0f
                    ? configBase
                    : ScopeHousingMeshSurgeryPlugin.CylinderRadius.Value * 2f;
                if (_baseScale < 0.001f) _baseScale = 0.030f;

                if (magnification < 1f) magnification = 1f;
                _lastMag = magnification;

                // ── Create world-space reticle object ─────────────────────
                DestroyReticleObject();
                _reticleObject = new GameObject("ScopeReticleOverlay");
                var mf = _reticleObject.AddComponent<MeshFilter>();
                mf.sharedMesh = _reticleMesh;
                _reticleObjectRenderer = _reticleObject.AddComponent<MeshRenderer>();
                _reticleObjectRenderer.sharedMaterial = _reticleMat;
                _reticleObjectRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _reticleObjectRenderer.receiveShadows = false;

                // Resolve the camera we align to now; OnPreCull will re-check if null.
                _alignedCamera = ScopeHousingMeshSurgeryPlugin.GetMainCamera();

                EnsurePreCullHook();

                _settled         = true;
                _alignmentActive = true;

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[Reticle] Showing: base={_baseScale:F4} mag={magnification:F1}x (world-space MeshRenderer)");
            }
            catch (System.Exception e)
            {
                ScopeHousingMeshSurgeryPlugin.LogError($"[Reticle] Show failed: {e.Message}");
            }
        }

        /// <summary>
        /// Per-frame update from ScopeLifecycle.Tick(): tracks magnification changes.
        /// </summary>
        public static void UpdateTransform(float magnification)
        {
            if (_reticleObject == null) return;
            if (magnification < 1f) magnification = 1f;
            if (Mathf.Abs(magnification - _lastMag) >= 0.01f)
                _lastMag = magnification;
        }

        /// <summary>Legacy wrapper.</summary>
        public static void UpdateScale(float magnification) => UpdateTransform(magnification);

        public static void Hide()
        {
            _alignmentActive = false;
            _settled         = false;
            if (_reticleObject != null)
                _reticleObject.SetActive(false);
        }

        public static void Cleanup()
        {
            Hide();
            RemovePreCullHook();
            DestroyReticleObject();
            _savedMarkTex      = null;
            _savedMaskTex      = null;
            _opticTransform    = null;
            _backLensTransform = null;
            _reticleAnchor     = null;
            _lensRenderer      = null;
            _alignedCamera     = null;
            _lastMag           = 1f;
            _baseScale         = 0f;
            _settled           = false;
            _weaponScaleOffset = Vector2.zero;
        }

        /// <summary>True while scoped and camera alignment is active.</summary>
        public static bool IsAlignmentActive => _alignmentActive && _settled;

        /// <summary>
        /// Current weapon-scale NDC offset; read by ScopeEffectsRenderer to
        /// shift vignette/shadow by the same amount as the scope housing.
        /// </summary>
        public static Vector2 WeaponScaleOffset => _weaponScaleOffset;

        /// <summary>Optic transform; shared with ScopeEffectsRenderer.</summary>
        public static Transform OpticTransform => _opticTransform;

        // ── OnPreCull hook ────────────────────────────────────────────────────

        private static void EnsurePreCullHook()
        {
            if (!_preCullRegistered)
            {
                Camera.onPreCull  += OnPreCullCallback;
                _preCullRegistered = true;
            }
        }

        private static void RemovePreCullHook()
        {
            if (_preCullRegistered)
            {
                Camera.onPreCull  -= OnPreCullCallback;
                _preCullRegistered = false;
            }
        }

        private static void OnPreCullCallback(Camera cam)
        {
            // Lazily resolve main camera (handles scene changes).
            if (_alignedCamera == null)
                _alignedCamera = ScopeHousingMeshSurgeryPlugin.GetMainCamera();
            if (cam != _alignedCamera) return;
            if (!_settled || !_alignmentActive) return;

            // ── Camera alignment ──────────────────────────────────────────
            // Override camera rotation to look exactly where the scope points.
            // Must happen in OnPreCull — after all game-system LateUpdates but
            // before Unity culls and renders the frame.
            Transform swaySource = PiPDisabler.OpticCameraTransform ?? _opticTransform;
            if (swaySource != null)
                cam.transform.rotation = swaySource.rotation;

            // ── Weapon-scale offset (for ScopeEffectsRenderer) ────────────
            if (Patches.WeaponScalingPatch.IsScalingActive)
            {
                Transform anchor = _backLensTransform
                                ?? PiPDisabler.OpticCameraTransform
                                ?? _reticleAnchor
                                ?? _opticTransform;
                Vector3 worldPoint = anchor != null ? anchor.position : Vector3.zero;
                Vector3 vp = cam.WorldToViewportPoint(worldPoint);
                _weaponScaleOffset = vp.z > 0f
                    ? new Vector2((vp.x - 0.5f) * 2f, (vp.y - 0.5f) * 2f)
                    : Vector2.zero;
            }
            else
            {
                _weaponScaleOffset = Vector2.zero;
            }

            // ── Reticle object transform ──────────────────────────────────
            // Set position/rotation/scale here (after LateUpdate, before render)
            // so the mesh is at the correct pose for this frame's draw.
            if (_reticleObject != null && _reticleObject.activeSelf)
                UpdateReticleTransform(cam);
        }

        private static void UpdateReticleTransform(Camera cam)
        {
            // Position: at the back-lens aperture, or a fixed distance in front
            // of the camera (along the now-scope-aligned forward) as fallback.
            const float referenceLensDistance = 0.075f;
            Vector3 pos;
            float   dist;

            if (_backLensTransform != null)
            {
                pos  = _backLensTransform.position;
                dist = Vector3.Distance(cam.transform.position, pos);
            }
            else
            {
                dist = referenceLensDistance;
                pos  = cam.transform.position + cam.transform.forward * dist;
            }
            dist = Mathf.Max(0.05f, dist);

            // World size derivation:
            //   ndcSize     = (_baseScale / mag) / refDist / tan(fovV/2)
            //   worldSize   = ndcSize * dist * tan(fovV/2)
            //               = (_baseScale / mag) / refDist * dist
            // tan(fovV/2) cancels → result is FOV-independent → SFP reticle
            // (same apparent size at all zoom levels, matching EFT scope behaviour).
            float mag       = Mathf.Max(1f, _lastMag);
            float worldSize = (_baseScale / mag) / referenceLensDistance * dist;
            worldSize = Mathf.Clamp(worldSize, 0.001f, 2f);

            // Orient the quad to face the (scope-aligned) camera.
            _reticleObject.transform.position   = pos;
            _reticleObject.transform.rotation   = cam.transform.rotation;
            _reticleObject.transform.localScale  = new Vector3(worldSize, worldSize, 1f);
        }

        // ── Reticle object lifetime ───────────────────────────────────────────

        private static void DestroyReticleObject()
        {
            if (_reticleObject != null)
            {
                UnityEngine.Object.Destroy(_reticleObject);
                _reticleObject         = null;
                _reticleObjectRenderer = null;
            }
        }

        // ── Mesh / material helpers ───────────────────────────────────────────

        private static void ApplyHorizontalFlip()
        {
            if (_reticleMesh == null) return;
            bool flip = ScopeHousingMeshSurgeryPlugin.ReticleFlipHorizontal.Value;
            _reticleMesh.uv = flip
                ? new[] { new Vector2(1,0), new Vector2(0,0), new Vector2(0,1), new Vector2(1,1) }
                : new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
        }

        private static void EnsureMeshAndMaterial()
        {
            if (_reticleMesh == null)
            {
                _reticleMesh = new Mesh { name = "ReticleQuad" };
                _reticleMesh.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                    new Vector3(0.5f,  0.5f,  0), new Vector3(-0.5f, 0.5f, 0)
                };
                _reticleMesh.uv = new[]
                {
                    new Vector2(0, 0), new Vector2(1, 0),
                    new Vector2(1, 1), new Vector2(0, 1)
                };
                _reticleMesh.triangles = new[] { 0,2,1, 0,3,2, 0,1,2, 0,2,3 };
                _reticleMesh.normals = new[]
                {
                    -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward
                };
                _reticleMesh.RecalculateBounds();
            }

            if (_reticleMat == null)
            {
                Shader alphaShader =
                    Shader.Find("Sprites/Default")         ??
                    Shader.Find("UI/Default")              ??
                    Shader.Find("Unlit/Transparent")       ??
                    Shader.Find("Particles/Alpha Blended") ??
                    Shader.Find("Legacy Shaders/Transparent/Diffuse");

                if (alphaShader == null)
                {
                    alphaShader =
                        Shader.Find("Particles/Additive") ??
                        Shader.Find("Legacy Shaders/Particles/Additive");

                    ScopeHousingMeshSurgeryPlugin.LogWarn(
                        "[Reticle] No alpha-blend shader found; falling back to Particles/Additive.");
                }

                _reticleMat = new Material(alphaShader)
                {
                    color       = Color.white,
                    renderQueue = 3100
                };
                _reticleMat.SetInt("_ZTest", (int)CompareFunction.Always);
                _reticleMat.SetInt("_ZWrite", 0);

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[Reticle] Created material (shader='{(alphaShader != null ? alphaShader.name : "null")}')");
            }

            ApplyHorizontalFlip();
        }
    }
}
