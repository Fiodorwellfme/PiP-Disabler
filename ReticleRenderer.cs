using System.Collections.Generic;
using EFT.CameraControl;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Renders the scope reticle via a CommandBuffer injected at
    /// CameraEvent.AfterForwardAlpha on the main FPS camera.
    ///
    /// ── CAMERA ALIGNMENT APPROACH ───────────────────────────────────────
    /// The root cause of reticle jitter is the mismatch between where the
    /// camera looks and where the scope tube points.  In vanilla PiP, this
    /// doesn't matter — the optic camera is aligned to the scope by design.
    /// In no-PiP mode, the main camera and scope have slightly different
    /// orientations, and any reticle placement (world-space, angular, etc.)
    /// amplifies that difference at tighter FOV values.
    ///
    /// The fix: in onPreCull (after all game systems have run), override the
    /// main camera's rotation to match the scope's forward direction.  This
    /// makes the rendered frame look exactly where the scope points.  The
    /// reticle becomes a simple fixed quad at screen center — zero jitter
    /// by definition.
    ///
    /// Weapon sway is preserved: the scope transform sways each frame from
    /// ProceduralWeaponAnimation, and the camera follows.  The player sees
    /// the world shift (exactly like looking through a real scope), not a
    /// dancing crosshair.
    ///
    /// ── NVG INTEGRATION ─────────────────────────────────────────────────
    /// The CommandBuffer is attached at CameraEvent.AfterForwardAlpha, which
    /// fires before OnRenderImage.  This means the NightVision image effect
    /// reads the reticle pixels as part of the scene colour buffer and applies
    /// its green tint, noise, and circular mask to them naturally.
    ///
    /// SetRenderTarget is intentionally absent from RebuildCommandBuffer.
    /// At AfterForwardAlpha the active RT is the scene colour buffer — correct.
    /// Explicitly binding CameraTarget here would resolve to the wrong surface
    /// under DLSS/FSR and cause the draw to silently disappear.
    /// </summary>
    internal static class ReticleRenderer
    {
        private static Material     _reticleMat;
        private static Mesh         _reticleMesh;
        private static Texture      _savedMarkTex;
        private static Texture      _savedMaskTex;

        // Stencil masking — housing occlusion
        // UI/Default exposes _Stencil, _StencilComp, _StencilOp, _ColorMask which let us
        // write to the stencil buffer and test against it without a custom shader.
        private static Material            _stencilClearMat;   // full-screen quad: write 0 to stencil
        private static Material            _housingStencilMat; // housing pass: write 1 to stencil, no colour
        private static Material            _stencilDebugMat;   // debug overlay: red tint where housing masks
        private static readonly List<Renderer> _housingRenderers = new List<Renderer>();
        private static bool                _hasStencilSupport; // true when UI/Default was found

        // Debug frame counter — logs stencil state for first N frames after scope enter
        private static int  _debugFrameCount;
        private const  int  DebugLogFrames = 10;

        // Scale tracking
        private static float _baseScale;
        private static float _lastMag = 1f;

        // Cached transforms
        private static Transform _opticTransform;   // OpticSight   — for forward (downrange)

        // CommandBuffer state
        private static CommandBuffer _cmdBuffer;
        private static Camera        _attachedCamera;
        private static bool          _preCullRegistered;

        // World-space TRS for the reticle quad (rebuilt in onPreCull)
        private static Matrix4x4 _reticleMatrix = Matrix4x4.identity;

        // Rendering state
        private static bool _settled;

        // Camera alignment state
        private static bool _alignmentActive;
        private static bool _reticleSuppressedByMask;

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Extract reticle textures from the OpticSight's lens material.
        /// MUST be called BEFORE LensTransparency replaces the mesh.
        /// </summary>
        public static void ExtractReticle(OpticSight os)
        {
            if (!ScopeHousingMeshSurgeryPlugin.GetShowReticle()) return;
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
                }

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[Reticle] Extracted: _MarkTex={(_savedMarkTex != null ? _savedMarkTex.name : "null")} " +
                    $"({(_savedMarkTex != null ? $"{_savedMarkTex.width}x{_savedMarkTex.height}" : "?")}) " +
                    $"_MaskTex={(_savedMaskTex != null ? _savedMaskTex.name : "null")} " +
                    "filter=Trilinear aniso=16");
            }
            catch (System.Exception e)
            {
                ScopeHousingMeshSurgeryPlugin.LogError($"[Reticle] Extract failed: {e.Message}");
            }
        }

        /// <summary>
        /// Show the reticle.  Creates the mesh/material, attaches the CommandBuffer
        /// to the main camera, and registers the onPreCull hook.
        /// </summary>
        public static void Show(OpticSight os, float magnification = 1f)
        {
            if (!ScopeHousingMeshSurgeryPlugin.GetShowReticle()) return;
            if (_savedMarkTex == null || os == null) return;

            try
            {
                _opticTransform = os.transform;

                EnsureMeshAndMaterial();

                _reticleMat.mainTexture = _savedMarkTex;
                ApplyHorizontalFlip();

                // Scale
                float configBase = ScopeHousingMeshSurgeryPlugin.GetReticleBaseSize();
                _baseScale = (configBase > 0f)
                    ? configBase
                    : ScopeHousingMeshSurgeryPlugin.GetCylinderRadius() * 2f;
                if (_baseScale < 0.001f) _baseScale = 0.030f;

                if (magnification < 1f) magnification = 1f;
                _lastMag = magnification;

                // Attach CommandBuffer + onPreCull
                AttachToCamera();

                _settled = true;
                _alignmentActive = true;

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[Reticle] Showing: base={_baseScale:F4} mag={magnification:F1}x " +
                    $"(camera-aligned centered rendering)");
            }
            catch (System.Exception e)
            {
                ScopeHousingMeshSurgeryPlugin.LogError($"[Reticle] Show failed: {e.Message}");
            }
        }

        /// <summary>
        /// Per-frame update from ScopeLifecycle.Tick():
        /// handles scale changes and ensures the CommandBuffer is attached.
        /// </summary>
        public static void UpdateTransform(float magnification)
        {
            if (_cmdBuffer == null) return;

            if (magnification < 1f) magnification = 1f;
            if (Mathf.Abs(magnification - _lastMag) >= 0.01f)
                _lastMag = magnification;

            var mainCam = ScopeHousingMeshSurgeryPlugin.GetMainCamera();
            if (mainCam != null && mainCam != _attachedCamera)
                AttachToCamera();
        }

        /// <summary>Legacy wrapper.</summary>
        public static void UpdateScale(float magnification) => UpdateTransform(magnification);

        public static void Hide()
        {
            _alignmentActive = false;
            _settled = false;
            _reticleSuppressedByMask = false;
            DetachFromCamera();
        }

        public static void Cleanup()
        {
            Hide();
            _housingRenderers.Clear();
            _debugFrameCount   = 0;
            _savedMarkTex      = null;
            _savedMaskTex      = null;
            _opticTransform    = null;
            _lastMag           = 1f;
            _baseScale         = 0f;
            _settled           = false;
            _reticleSuppressedByMask = false;
        }

        /// <summary>
        /// Returns true if camera alignment is currently active while scoped.
        /// Used by ScopeEffectsRenderer to know that vignette/shadow can also
        /// render centered rather than tracking lens position.
        /// </summary>
        public static bool IsAlignmentActive => _alignmentActive && _settled;

        /// <summary>
        /// Returns the current optic transform (for ScopeEffectsRenderer to
        /// share camera alignment).
        /// </summary>
        public static Transform OpticTransform => _opticTransform;

        /// <summary>
        /// Provide the scope housing renderers that will be drawn into the stencil
        /// buffer each frame so the reticle disappears wherever the housing covers it.
        /// Call after LensTransparency.HideAllLensSurfaces() so lens renderers are
        /// already empty-meshed and won't end up in the list.
        /// Pass null or an empty list to disable housing masking.
        /// </summary>
        public static void SetHousingRenderers(List<Renderer> renderers)
        {
            _housingRenderers.Clear();
            _debugFrameCount = 0; // reset so first frames of new scope are logged
            if (renderers != null)
                _housingRenderers.AddRange(renderers);

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[Reticle] Housing mask: {_housingRenderers.Count} renderer(s) registered" +
                $" stencilSupport={_hasStencilSupport}");
        }

        // ── CommandBuffer management ────────────────────────────────────────

        private static void AttachToCamera()
        {
            var mainCam = ScopeHousingMeshSurgeryPlugin.GetMainCamera();
            if (mainCam == null) return;

            if (_attachedCamera != null && _attachedCamera != mainCam)
                DetachFromCamera();

            if (_attachedCamera == mainCam) return;

            if (_cmdBuffer == null)
                _cmdBuffer = new CommandBuffer { name = "ScopeReticleOverlay" };

            mainCam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, _cmdBuffer);
            _attachedCamera = mainCam;

            if (!_preCullRegistered)
            {
                Camera.onPreCull += OnPreCullCallback;
                _preCullRegistered = true;
            }

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[Reticle] CommandBuffer attached to '{mainCam.name}' at AfterForwardAlpha");
        }

        private static void DetachFromCamera()
        {
            if (_preCullRegistered)
            {
                Camera.onPreCull -= OnPreCullCallback;
                _preCullRegistered = false;
            }

            if (_attachedCamera != null && _cmdBuffer != null)
            {
                try { _attachedCamera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, _cmdBuffer); }
                catch (System.Exception) { }
            }

            if (_cmdBuffer != null)
            {
                _cmdBuffer.Clear();
                _cmdBuffer.Release();
                _cmdBuffer = null;
            }

            _attachedCamera = null;
        }

        // ── onPreCull — camera alignment + rebuild CommandBuffer ─────────────

        private static void OnPreCullCallback(Camera cam)
        {
            if (cam != _attachedCamera) return;
            if (_cmdBuffer == null || _reticleMat == null || !_settled) return;

            // ── Camera alignment ─────────────────────────────────────────
            // Override the camera's rotation to look exactly where the scope
            // points.  This happens in onPreCull — after all game systems
            // (PWA, animation, IK) and OpticComponentUpdater.LateUpdate()
            // have updated transforms, but before Unity starts rendering.
            //
            // We use the optic camera's transform cached by PiPDisabler.
            // OpticComponentUpdater.LateUpdate() syncs this transform to the
            // scope's look direction every frame.  We let LateUpdate run
            // (v4.5.2 fix), so the transform is always up to date even though
            // the optic camera itself can't render.
            if (_alignmentActive)
            {
                // Primary source: optic camera transform kept in sync by EFT updater.
                // Fallback: optic transform itself, so sway-follow remains active even
                // if optic camera cache is temporarily unavailable.
                Transform swaySource = PiPDisabler.OpticCameraTransform ?? _opticTransform;
                if (swaySource != null)
                {
                    cam.transform.rotation = swaySource.rotation;
                }
            }

            RebuildMatrix(cam);
            RebuildCommandBuffer(cam);
        }

        // ── Centered quad matrix ─────────────────────────────────────────────

        /// <summary>
        /// Place the reticle quad at screen center: a fixed distance along
        /// the camera's (now scope-aligned) forward.  Since the camera is
        /// aligned with the scope, this is always dead center.
        ///
        /// Size is fixed in screen-space and does not react to current FOV or magnification.
        /// </summary>
        private static void RebuildMatrix(Camera cam)
        {
            if (cam == null) return;

            // Clip-space centered quad: independent of world/lens transforms.
            // Convert configured physical size using fixed references so the
            // reticle stays constant on screen regardless of runtime FOV/mag.
            const float referenceFovDeg = 35f;
            const float referenceLensDistance = 0.075f;
            float referenceTanHalfFov = Mathf.Max(0.01f, Mathf.Tan(referenceFovDeg * Mathf.Deg2Rad * 0.5f));

            float angularSize = _baseScale / referenceLensDistance;
            float ndcSize = angularSize / referenceTanHalfFov;
            ndcSize = Mathf.Clamp(ndcSize, 0.01f, 2f);

            Vector3 pos = new Vector3(0f, 0f, 0.5f);
            float aspect = GetSceneAspect(cam);
            Vector3 scale = new Vector3(ndcSize / Mathf.Max(0.01f, aspect), ndcSize, 1f);
            _reticleMatrix = Matrix4x4.TRS(pos, Quaternion.identity, scale);
        }

        /// <summary>
        /// Rebuild the CommandBuffer.
        ///
        /// When housing renderers are available and UI/Default was found (stencil-capable):
        ///   1. Clear stencil to 0 with a full-screen clip-space quad.
        ///   2. Draw housing meshes in world-space, writing 1 to stencil where the housing
        ///      passes the depth test (i.e. is actually visible).
        ///   3. Draw the reticle with stencil test NotEqual-1, so it is invisible wherever
        ///      the housing was just written.
        ///
        /// Falls back to the original single-draw path when stencil is unavailable or no
        /// housing renderers have been registered.
        ///
        /// Note: SetRenderTarget is intentionally absent.  At AfterForwardAlpha the active
        /// RT is already the scene colour buffer.  Explicitly binding CameraTarget resolves
        /// to the wrong surface under DLSS/FSR and silently drops all draws.
        /// </summary>
        private static void RebuildCommandBuffer(Camera cam)
        {
            _cmdBuffer.Clear();

            // SetViewport uses the camera's render-resolution pixel dimensions.
            // Do NOT use Screen.width/height here — under DLSS/FSR the camera renders
            // at a lower internal resolution and Screen reports the display resolution,
            // which would pin the viewport offset and mis-center the reticle.
            _cmdBuffer.SetViewport(GetSceneViewport(cam));

            bool useStencil = _hasStencilSupport && _housingRenderers.Count > 0
                              && _stencilClearMat != null && _housingStencilMat != null;

            if (useStencil)
                UpdateReticleSuppressionByMaskCoverage(cam);
            else
                _reticleSuppressedByMask = false;

            // ── Per-frame debug logging (first N frames after scope enter) ────────────
            if (_debugFrameCount < DebugLogFrames)
            {
                int activeCount = 0;
                for (int i = 0; i < _housingRenderers.Count; i++)
                {
                    var r = _housingRenderers[i];
                    if (r != null && r.gameObject.activeInHierarchy) activeCount++;
                }

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[Reticle] Frame {_debugFrameCount + 1}/{DebugLogFrames}: " +
                    $"useStencil={useStencil} housingTotal={_housingRenderers.Count} " +
                    $"housingActive={activeCount} stencilSupport={_hasStencilSupport}");
                _debugFrameCount++;
            }

            // Scale the unit quad (-0.5..0.5) to cover the full NDC range (-1..1).
            var fullScreenMatrix = Matrix4x4.TRS(
                Vector3.zero, Quaternion.identity, new Vector3(2f, 2f, 1f));

            if (useStencil)
            {
                // ── Step 1: clear stencil (clip-space full-screen quad) ──────────────
                _cmdBuffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                _cmdBuffer.DrawMesh(_reticleMesh, fullScreenMatrix, _stencilClearMat, 0, -1);

                // ── Step 2: write housing to stencil (world-space) ──────────────────
                _cmdBuffer.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
                for (int i = 0; i < _housingRenderers.Count; i++)
                {
                    var r = _housingRenderers[i];
                    if (r == null || !r.gameObject.activeInHierarchy) continue;
                    _cmdBuffer.DrawRenderer(r, _housingStencilMat);
                }

                // ── Step 3: draw reticle with stencil test (clip-space) ─────────────
                if (!_reticleSuppressedByMask)
                {
                    _cmdBuffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                    _cmdBuffer.DrawMesh(_reticleMesh, _reticleMatrix, _reticleMat, 0, -1);
                }

                // ── Step 4: optional debug overlay — red tint where housing masked ───
                // Renders anywhere stencil == 1, i.e. every pixel the housing suppressed.
                // Enable via DebugShowHousingMask in BepInEx config.
                if (ScopeHousingMeshSurgeryPlugin.GetDebugShowHousingMask()
                    && _stencilDebugMat != null)
                {
                    _cmdBuffer.DrawMesh(_reticleMesh, fullScreenMatrix, _stencilDebugMat, 0, -1);
                }
            }
            else
            {
                // Original path — no stencil.
                _cmdBuffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                _cmdBuffer.DrawMesh(_reticleMesh, _reticleMatrix, _reticleMat, 0, -1);
            }

            _cmdBuffer.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
        }

        // ── Private helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the viewport rect in render-resolution pixel coordinates.
        /// cam.pixelWidth/pixelHeight always reflect the actual RT size, even
        /// under DLSS/FSR where Screen.width/height report display resolution.
        /// </summary>
        private static Rect GetSceneViewport(Camera cam)
        {
            return new Rect(0f, 0f,
                Mathf.Max(1f, cam.pixelWidth),
                Mathf.Max(1f, cam.pixelHeight));
        }

        /// <summary>
        /// Returns the aspect ratio of the scene render target.
        /// Used to compensate the reticle quad's X scale so it stays square.
        /// </summary>
        private static float GetSceneAspect(Camera cam)
        {
            return Mathf.Max(0.01f, cam.pixelWidth / Mathf.Max(1f, cam.pixelHeight));
        }

        private static void UpdateReticleSuppressionByMaskCoverage(Camera cam)
        {
            Rect reticleViewportRect = GetReticleViewportRect();
            float maskedPercent = EstimateReticleMaskedPercent(cam, reticleViewportRect);
            float visiblePercent = 100f - maskedPercent;

            float hideIfMaskedPercent = ScopeHousingMeshSurgeryPlugin.GetReticleHideIfMaskedPercent();
            float showIfVisiblePercent = ScopeHousingMeshSurgeryPlugin.GetReticleShowIfVisiblePercent();

            if (!_reticleSuppressedByMask)
            {
                if (maskedPercent >= hideIfMaskedPercent)
                    _reticleSuppressedByMask = true;
            }
            else
            {
                if (visiblePercent >= showIfVisiblePercent)
                    _reticleSuppressedByMask = false;
            }
        }

        private static Rect GetReticleViewportRect()
        {
            float centerX = 0.5f + _reticleMatrix.m03;
            float centerY = 0.5f + _reticleMatrix.m13;
            float width = Mathf.Abs(_reticleMatrix.m00);
            float height = Mathf.Abs(_reticleMatrix.m11);
            return Rect.MinMaxRect(centerX - width * 0.5f, centerY - height * 0.5f, centerX + width * 0.5f, centerY + height * 0.5f);
        }

        private static float EstimateReticleMaskedPercent(Camera cam, Rect reticleViewportRect)
        {
            if (reticleViewportRect.width <= 0.0001f || reticleViewportRect.height <= 0.0001f)
                return 0f;

            const int sampleGrid = 24;
            int maskedCount = 0;
            int totalCount = sampleGrid * sampleGrid;

            List<Rect> projectedRects = BuildProjectedHousingRects(cam);
            if (projectedRects.Count == 0)
                return 0f;

            for (int y = 0; y < sampleGrid; y++)
            {
                float fy = (y + 0.5f) / sampleGrid;
                float vy = Mathf.Lerp(reticleViewportRect.yMin, reticleViewportRect.yMax, fy);

                for (int x = 0; x < sampleGrid; x++)
                {
                    float fx = (x + 0.5f) / sampleGrid;
                    float vx = Mathf.Lerp(reticleViewportRect.xMin, reticleViewportRect.xMax, fx);

                    if (IsPointInsideAnyRect(vx, vy, projectedRects))
                        maskedCount++;
                }
            }

            return (maskedCount * 100f) / Mathf.Max(1, totalCount);
        }

        private static bool IsPointInsideAnyRect(float vx, float vy, List<Rect> rects)
        {
            for (int i = 0; i < rects.Count; i++)
            {
                if (rects[i].Contains(new Vector2(vx, vy)))
                    return true;
            }

            return false;
        }

        private static List<Rect> BuildProjectedHousingRects(Camera cam)
        {
            var rects = new List<Rect>(_housingRenderers.Count);
            for (int i = 0; i < _housingRenderers.Count; i++)
            {
                Renderer r = _housingRenderers[i];
                if (r == null || !r.gameObject.activeInHierarchy) continue;

                Bounds b = r.bounds;
                Vector3 c = b.center;
                Vector3 e = b.extents;

                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;
                bool hasFrontPoint = false;

                for (int cx = -1; cx <= 1; cx += 2)
                for (int cy = -1; cy <= 1; cy += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    Vector3 p = new Vector3(c.x + e.x * cx, c.y + e.y * cy, c.z + e.z * cz);
                    Vector3 vp = cam.WorldToViewportPoint(p);
                    if (vp.z <= 0f) continue;

                    hasFrontPoint = true;
                    minX = Mathf.Min(minX, vp.x);
                    minY = Mathf.Min(minY, vp.y);
                    maxX = Mathf.Max(maxX, vp.x);
                    maxY = Mathf.Max(maxY, vp.y);
                }

                if (!hasFrontPoint) continue;
                rects.Add(Rect.MinMaxRect(minX, minY, maxX, maxY));
            }

            return rects;
        }

        private static void ApplyHorizontalFlip()
        {
            if (_reticleMesh == null) return;
            _reticleMesh.uv = new[]
            {
                new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1)
            };
        }

        private static void EnsureMeshAndMaterial()
        {
            if (_reticleMesh != null && _reticleMat != null) return;

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
                // UI/Default exposes _Stencil* and _ColorMask, so it must be found first
                // for stencil-based housing masking.  Sprites/Default does not have these.
                Shader stencilShader = Shader.Find("UI/Default");
                _hasStencilSupport   = stencilShader != null;

                Shader alphaShader =
                    stencilShader                           ??
                    Shader.Find("Sprites/Default")          ??
                    Shader.Find("Unlit/Transparent")        ??
                    Shader.Find("Particles/Alpha Blended")  ??
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
                _reticleMat.SetInt("_ZTest",  (int)CompareFunction.Always);
                _reticleMat.SetInt("_ZWrite", 0);

                // ── Stencil test: only draw reticle where housing did NOT write ──────
                // We use ref=1.  Housing writes 1 → reticle skips those pixels.
                if (_hasStencilSupport)
                {
                    _reticleMat.SetFloat("_Stencil",          1f);
                    _reticleMat.SetFloat("_StencilComp",      (float)CompareFunction.NotEqual); // pass ≠ 1
                    _reticleMat.SetFloat("_StencilOp",        (float)StencilOp.Keep);
                    _reticleMat.SetFloat("_StencilReadMask",  255f);
                    _reticleMat.SetFloat("_StencilWriteMask", 0f);   // don't write
                }

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[Reticle] Created material (shader='{(alphaShader != null ? alphaShader.name : "null")}'" +
                    $" stencilSupport={_hasStencilSupport})");

                // ── Stencil helper materials (both need UI/Default) ──────────────────
                if (_hasStencilSupport)
                {
                    // Clear material: full-screen pass, writes stencil=0, no colour output.
                    _stencilClearMat = new Material(stencilShader) { renderQueue = 4998 };
                    _stencilClearMat.SetFloat("_Stencil",          0f);
                    _stencilClearMat.SetFloat("_StencilComp",      (float)CompareFunction.Always);
                    _stencilClearMat.SetFloat("_StencilOp",        (float)StencilOp.Replace);
                    _stencilClearMat.SetFloat("_StencilWriteMask", 255f);
                    _stencilClearMat.SetFloat("_ColorMask",        0f); // write no colour
                    _stencilClearMat.SetInt("_ZTest",  (int)CompareFunction.Always);
                    _stencilClearMat.SetInt("_ZWrite", 0);

                    // Housing material: world-space pass, writes stencil=1 where depth passes.
                    _housingStencilMat = new Material(stencilShader) { renderQueue = 4999 };
                    _housingStencilMat.SetFloat("_Stencil",          1f);
                    _housingStencilMat.SetFloat("_StencilComp",      (float)CompareFunction.Always);
                    _housingStencilMat.SetFloat("_StencilOp",        (float)StencilOp.Replace);
                    _housingStencilMat.SetFloat("_StencilWriteMask", 255f);
                    _housingStencilMat.SetFloat("_ColorMask",        0f); // write no colour
                    _housingStencilMat.SetInt("_ZTest",  (int)CompareFunction.LessEqual); // only where visible
                    _housingStencilMat.SetInt("_ZWrite", 0);

                    // Debug overlay: renders a semi-transparent red tint wherever stencil == 1.
                    // Reveals which screen regions are being suppressed by the housing mask.
                    _stencilDebugMat = new Material(stencilShader)
                    {
                        color       = new Color(1f, 0.1f, 0.1f, 0.55f),
                        renderQueue = 5000
                    };
                    _stencilDebugMat.SetFloat("_Stencil",         1f);
                    _stencilDebugMat.SetFloat("_StencilComp",     (float)CompareFunction.Equal); // only where housing is
                    _stencilDebugMat.SetFloat("_StencilOp",       (float)StencilOp.Keep);
                    _stencilDebugMat.SetFloat("_StencilReadMask", 255f);
                    _stencilDebugMat.SetInt("_ZTest",  (int)CompareFunction.Always);
                    _stencilDebugMat.SetInt("_ZWrite", 0);
                }
            }

            ApplyHorizontalFlip();
        }
    }
}
