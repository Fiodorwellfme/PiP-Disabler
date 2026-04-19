using System.Collections.Generic;
using EFT.CameraControl;
using UnityEngine;
using UnityEngine.Rendering;

namespace PiPDisabler
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

        // Stencil masking — lens visibility
        // UI/Default exposes _Stencil, _StencilComp, _StencilOp, _ColorMask which let us
        // write to the stencil buffer and test against it without a custom shader.
        private static Material            _stencilClearMat; // full-screen quad: write 0 to stencil
        private static Material            _lensStencilMat;  // lens pass: write 1 to stencil, no colour
        private static Material            _occluderStencilMat; // housing/weapon pass: write 2 to stencil
        private static Material            _stencilDebugMat; // debug overlay: red tint where lens writes
        private static readonly List<LensTransparency.LensMaskEntry> _lensMaskEntries = new List<LensTransparency.LensMaskEntry>();
        private static readonly List<Renderer> _occluderMaskRenderers = new List<Renderer>();
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
        private static CameraEvent   _attachedEvent = CameraEvent.AfterForwardAlpha;
        private static bool          _preCullRegistered;

        // World-space TRS for the reticle quad (rebuilt in onPreCull)
        private static Matrix4x4 _reticleMatrix = Matrix4x4.identity;

        // Rendering state
        private static bool _settled;
        private static bool _stencilOnlyPersistence;

        // Camera alignment state
        private static bool _alignmentActive;

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Extract reticle textures from the OpticSight's lens material.
        /// MUST be called BEFORE LensTransparency replaces the mesh.
        /// </summary>
        public static void ExtractReticle(OpticSight os)
        {
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

                PiPDisablerPlugin.LogSource.LogInfo(
                    $"[Reticle] Extracted: _MarkTex={(_savedMarkTex != null ? _savedMarkTex.name : "null")} " +
                    $"({(_savedMarkTex != null ? $"{_savedMarkTex.width}x{_savedMarkTex.height}" : "?")}) " +
                    $"_MaskTex={(_savedMaskTex != null ? _savedMaskTex.name : "null")} " +
                    "filter=Trilinear aniso=16");
            }
            catch (System.Exception e)
            {
                PiPDisablerPlugin.LogSource.LogInfo($"[Reticle] Extract failed: {e.Message}");
            }
        }

        /// <summary>
        /// Show the reticle.  Creates the mesh/material, attaches the CommandBuffer
        /// to the main camera, and registers the onPreCull hook.
        /// </summary>
        public static void Show(OpticSight os, float magnification = 1f)
        {
            if (_savedMarkTex == null || os == null) return;

            try
            {
                _opticTransform = os.transform;

                EnsureMeshAndMaterial();

                _reticleMat.mainTexture = _savedMarkTex;
                ApplyHorizontalFlip();

                // Scale
                float configBase = PerScopeMeshSurgerySettings.GetReticleBaseSize();
                _baseScale = (configBase > 0f)
                    ? configBase
                    : PerScopeMeshSurgerySettings.GetPlane1Radius() * 2f;
                if (_baseScale < 0.001f) _baseScale = 0.030f;

                if (magnification < 1f) magnification = 1f;
                _lastMag = magnification;

                // Attach CommandBuffer + onPreCull
                AttachToCamera();

                _stencilOnlyPersistence = false;
                _settled = true;
                _alignmentActive = true;

                PiPDisablerPlugin.LogSource.LogInfo(
                    $"[Reticle] Showing: base={_baseScale:F4} mag={magnification:F1}x " +
                    $"(camera-aligned centered rendering)");
            }
            catch (System.Exception e)
            {
                PiPDisablerPlugin.LogSource.LogInfo($"[Reticle] Show failed: {e.Message}");
            }
        }

        /// <summary>
        /// Per-frame update from ScopeLifecycle.Tick():
        /// handles scale changes and ensures the CommandBuffer is attached.
        /// </summary>
        public static void UpdateTransform(float magnification)
        {
            if (_cmdBuffer == null) return;

            EnsureCorrectCameraEvent();

            if (magnification < 1f) magnification = 1f;
            if (Mathf.Abs(magnification - _lastMag) >= 0.01f)
                _lastMag = magnification;

            var mainCam = Helpers.GetMainCamera();
            if (mainCam != null && mainCam != _attachedCamera)
                AttachToCamera();
        }

        public static void Hide()
        {
            _alignmentActive = false;
            _settled = false;
            _stencilOnlyPersistence = false;
            DetachFromCamera();
        }

        public static void OnScopeExit(bool keepStencil)
        {
            _alignmentActive = false;
            _settled = false;

            if (keepStencil && _cmdBuffer != null)
            {
                _stencilOnlyPersistence = true;
                return;
            }

            Cleanup();
        }

        public static void StopStencilOnlyPersistence()
        {
            if (!_stencilOnlyPersistence) return;
            Cleanup();
        }

        public static void Cleanup()
        {
            Hide();
            _lensMaskEntries.Clear();
            _occluderMaskRenderers.Clear();
            _debugFrameCount   = 0;
            _savedMarkTex      = null;
            _savedMaskTex      = null;
            _opticTransform    = null;
            _lastMag           = 1f;
            _baseScale         = 0f;
            _settled           = false;
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

        public static CameraEvent CurrentCameraEvent => _attachedEvent;

        /// <summary>
        /// Provide cached lens mesh entries for the stencil pass.
        /// Pass null or an empty list to disable lens masking.
        /// </summary>
        public static void SetLensMaskEntries(List<LensTransparency.LensMaskEntry> entries)
        {
            _lensMaskEntries.Clear();
            _debugFrameCount = 0;
            if (entries != null)
                _lensMaskEntries.AddRange(entries);

            PiPDisablerPlugin.LogSource.LogInfo(
                $"[Reticle] Lens mask: {_lensMaskEntries.Count} entry(s) registered" +
                $" stencilSupport={_hasStencilSupport}");
        }

        public static void SetOccluderMaskRenderers(List<Renderer> renderers)
        {
            _occluderMaskRenderers.Clear();
            if (renderers != null)
                _occluderMaskRenderers.AddRange(renderers);
        }

        // ── CommandBuffer management ────────────────────────────────────────

        private static void AttachToCamera()
        {
            var mainCam = Helpers.GetMainCamera();
            if (mainCam == null) return;

            if (_attachedCamera != null && _attachedCamera != mainCam)
                DetachFromCamera();

            if (_attachedCamera == mainCam) return;

            if (_cmdBuffer == null)
                _cmdBuffer = new CommandBuffer { name = "ScopeReticleOverlay" };

            CameraEvent targetEvent = GetReticleCameraEvent();
            mainCam.AddCommandBuffer(targetEvent, _cmdBuffer);
            _attachedCamera = mainCam;
            _attachedEvent = targetEvent;

            if (!_preCullRegistered)
            {
                Camera.onPreCull += OnPreCullCallback;
                _preCullRegistered = true;
            }

            PiPDisablerPlugin.LogSource.LogInfo(
                $"[Reticle] CommandBuffer attached to '{mainCam.name}' at {_attachedEvent}");
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
                try { _attachedCamera.RemoveCommandBuffer(_attachedEvent, _cmdBuffer); }
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


        private static CameraEvent GetReticleCameraEvent()
        {
            if (Settings.DebugReticleAfterEverything.Value)
                return CameraEvent.AfterEverything;
            bool nvgOn = Shader.GetGlobalFloat("_NightVisionOn") > 0.5f;
            return nvgOn ? CameraEvent.AfterForwardAlpha : CameraEvent.AfterEverything;
        }

        private static void EnsureCorrectCameraEvent()
        {
            if (_attachedCamera == null || _cmdBuffer == null) return;

            CameraEvent desiredEvent = GetReticleCameraEvent();
            if (desiredEvent == _attachedEvent) return;

            try { _attachedCamera.RemoveCommandBuffer(_attachedEvent, _cmdBuffer); }
            catch (System.Exception) { }

            _attachedCamera.AddCommandBuffer(desiredEvent, _cmdBuffer);
            _attachedEvent = desiredEvent;

            PiPDisablerPlugin.LogSource.LogInfo($"[Reticle] CommandBuffer moved to {_attachedEvent} (debug toggle)");
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
            if (_alignmentActive && !FreelookTracker.IsFreelooking)
            {
                // Primary source: optic camera transform kept in sync by EFT updater.
                // Fallback: optic transform itself, so sway-follow remains active even
                // if optic camera cache is temporarily unavailable.
                //
                // Skipped during freelook: the player is looking around independently
                // of the scope direction, so the camera must NOT be locked to the optic.
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
            float aspect = GetActiveAspect(cam);
            Vector3 scale = new Vector3(ndcSize / Mathf.Max(0.01f, aspect), ndcSize, 1f);
            _reticleMatrix = Matrix4x4.TRS(pos, Quaternion.identity, scale);
        }

        /// <summary>
        /// Rebuild the CommandBuffer.
        ///
        /// When cached lens meshes are available and UI/Default was found (stencil-capable):
        ///   1. Clear stencil to 0 with a full-screen clip-space quad.
        ///   2. Draw the cached lens meshes in world-space, writing 1 to stencil where
        ///      the lens passes the depth test.
        ///   3. Draw the reticle with stencil test Equal-1, so it only appears inside the
        ///      visible lens.
        ///
        /// Falls back to the original single-draw path when stencil is unavailable or no
        /// lens renderers have been registered.
        ///
        /// Note: when attached at AfterForwardAlpha we do NOT rebind the render target,
        /// because the active RT is already the scene colour buffer.  When attached at
        /// AfterEverything (debug mode), we explicitly bind CameraTarget + display viewport
        /// so clip-space quads map to the final upscaled frame.
        /// </summary>
        private static void RebuildCommandBuffer(Camera cam)
        {
            _cmdBuffer.Clear();

            bool isAfterEverything = _attachedEvent == CameraEvent.AfterEverything;

            if (isAfterEverything)
            {
                // Late-overlay path: draw in display space after upscaling/postfx.
                _cmdBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                _cmdBuffer.SetViewport(GetDisplayViewport(cam));
            }
            else
            {
                // Scene-overlay path: use render-resolution viewport for DLSS/FSR correctness.
                _cmdBuffer.SetViewport(GetSceneViewport(cam));
            }

            bool useStencil = _hasStencilSupport && _lensMaskEntries.Count > 0
                              && _stencilClearMat != null && _lensStencilMat != null;

            // ── Per-frame debug logging (first N frames after scope enter) ────────────
            if (_debugFrameCount < DebugLogFrames)
            {
                int activeCount = 0;
                for (int i = 0; i < _lensMaskEntries.Count; i++)
                {
                    var entry = _lensMaskEntries[i];
                    if (entry.Renderer != null && entry.Renderer.gameObject.activeInHierarchy) activeCount++;
                }

                PiPDisablerPlugin.LogSource.LogInfo(
                    $"[Reticle] Frame {_debugFrameCount + 1}/{DebugLogFrames}: " +
                    $"useStencil={useStencil} lensTotal={_lensMaskEntries.Count} " +
                    $"lensActive={activeCount} stencilSupport={_hasStencilSupport}");
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

                // ── Step 2: write lens visibility to stencil (world-space) ──────────
                _cmdBuffer.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
                for (int i = 0; i < _occluderMaskRenderers.Count; i++)
                    DrawOccluderMaskRenderer(_occluderMaskRenderers[i]);
                for (int i = 0; i < _lensMaskEntries.Count; i++)
                {
                    var entry = _lensMaskEntries[i];
                    DrawLensMaskEntry(entry);
                }

                if (!_stencilOnlyPersistence)
                {
                    // ── Step 3: draw reticle only inside the visible lens (clip-space) ──
                    _cmdBuffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                    _cmdBuffer.DrawMesh(_reticleMesh, _reticleMatrix, _reticleMat, 0, -1);
                }

                // ── Step 4: optional debug overlay — red tint where lens writes ─────
                // Renders anywhere stencil == 1, i.e. every visible lens pixel.
                // Enable via DebugShowHousingMask in BepInEx config.
                if (Settings.DebugShowHousingMask.Value && _stencilDebugMat != null)
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

        private static void DrawLensMaskEntry(LensTransparency.LensMaskEntry entry)
        {
            if (entry.Renderer == null || entry.Mesh == null) return;
            if (!entry.Renderer.gameObject.activeInHierarchy) return;

            int subMeshCount = Mathf.Max(1, entry.Mesh.subMeshCount);
            Matrix4x4 matrix = entry.Renderer.localToWorldMatrix;
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                _cmdBuffer.DrawMesh(entry.Mesh, matrix, _lensStencilMat, subMesh, -1);
        }

        private static void DrawOccluderMaskRenderer(Renderer renderer)
        {
            if (renderer == null) return;
            if (!renderer.gameObject.activeInHierarchy) return;

            var mf = renderer.GetComponent<MeshFilter>();
            var smr = renderer as SkinnedMeshRenderer;
            Mesh mesh = mf?.sharedMesh ?? smr?.sharedMesh;
            if (mesh == null) return;

            int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            Matrix4x4 matrix = renderer.localToWorldMatrix;
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                _cmdBuffer.DrawMesh(mesh, matrix, _occluderStencilMat, subMesh, -1);
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

        private static Rect GetDisplayViewport(Camera cam)
            => Helpers.GetDisplayViewport(cam);

        /// <summary>
        /// Returns the aspect ratio for the currently attached command-buffer event.
        /// </summary>
        private static float GetActiveAspect(Camera cam)
        {
            if (_attachedEvent == CameraEvent.AfterEverything)
            {
                Rect r = GetDisplayViewport(cam);
                return Mathf.Max(0.01f, r.width / Mathf.Max(1f, r.height));
            }

            return Mathf.Max(0.01f, cam.pixelWidth / Mathf.Max(1f, cam.pixelHeight));
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
                // for stencil-based lens masking.  Sprites/Default does not have these.
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

                    PiPDisablerPlugin.LogSource.LogInfo(
                        "[Reticle] No alpha-blend shader found; falling back to Particles/Additive.");
                }

                _reticleMat = new Material(alphaShader)
                {
                    color       = Color.white,
                    renderQueue = 3100
                };
                _reticleMat.SetInt("_ZTest",  (int)CompareFunction.Always);
                _reticleMat.SetInt("_ZWrite", 0);

                // ── Stencil test: only draw reticle where the lens DID write ─────────
                if (_hasStencilSupport)
                {
                    _reticleMat.SetFloat("_Stencil",          1f);
                    _reticleMat.SetFloat("_StencilComp",      (float)CompareFunction.Equal);
                    _reticleMat.SetFloat("_StencilOp",        (float)StencilOp.Keep);
                    _reticleMat.SetFloat("_StencilReadMask",  255f);
                    _reticleMat.SetFloat("_StencilWriteMask", 0f);   // don't write
                }

                PiPDisablerPlugin.LogSource.LogInfo(
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

                    // Lens material: world-space pass, writes stencil=1 where the lens is visible.
                    _lensStencilMat = new Material(stencilShader) { renderQueue = 4999 };
                    _lensStencilMat.SetFloat("_Stencil",          1f);
                    _lensStencilMat.SetFloat("_StencilComp",      (float)CompareFunction.Always);
                    _lensStencilMat.SetFloat("_StencilOp",        (float)StencilOp.Replace);
                    _lensStencilMat.SetFloat("_StencilWriteMask", 255f);
                    _lensStencilMat.SetFloat("_ColorMask",        0f);
                    _lensStencilMat.SetInt("_ZTest",  (int)CompareFunction.LessEqual);
                    _lensStencilMat.SetInt("_ZWrite", 0);

                    _occluderStencilMat = new Material(stencilShader) { renderQueue = 4998 };
                    _occluderStencilMat.SetFloat("_Stencil",          2f);
                    _occluderStencilMat.SetFloat("_StencilComp",      (float)CompareFunction.Always);
                    _occluderStencilMat.SetFloat("_StencilOp",        (float)StencilOp.Replace);
                    _occluderStencilMat.SetFloat("_StencilWriteMask", 255f);
                    _occluderStencilMat.SetFloat("_ColorMask",        0f);
                    _occluderStencilMat.SetInt("_ZTest",  (int)CompareFunction.LessEqual);
                    _occluderStencilMat.SetInt("_ZWrite", 0);

                    // Debug overlay: renders a semi-transparent red tint wherever stencil == 1.
                    // Reveals which screen regions are inside the visible lens mask.
                    _stencilDebugMat = new Material(stencilShader)
                    {
                        color       = new Color(1f, 0.1f, 0.1f, 0.55f),
                        renderQueue = 5000
                    };
                    _stencilDebugMat.SetFloat("_Stencil",         1f);
                    _stencilDebugMat.SetFloat("_StencilComp",     (float)CompareFunction.Equal); // only where the lens is visible
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
