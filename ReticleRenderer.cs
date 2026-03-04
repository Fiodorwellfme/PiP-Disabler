using EFT.CameraControl;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Renders the scope reticle via a CommandBuffer injected at
    /// CameraEvent.AfterEverything on the main FPS camera.
    ///
    /// ── CAMERA ALIGNMENT APPROACH ───────────────────────────────────────
    /// The root cause of reticle jitter is the mismatch between where the
    /// camera looks and where the scope tube points.  In vanilla PiP, this
    /// doesn't matter — the optic camera is aligned to the scope by design.
    /// In no-PiP mode, the main camera and scope have slightly different
    /// orientations, and any reticle placement (world-space, angular, etc.)
    /// amplifies that difference at high magnification.
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
    /// </summary>
    internal static class ReticleRenderer
    {
        private static Material     _reticleMat;
        private static Mesh         _reticleMesh;
        private static Texture      _savedMarkTex;
        private static Texture      _savedMaskTex;

        // Scale tracking
        private static float _baseScale;
        private static float _lastMag = 1f;

        // Cached transforms
        private static Transform _opticTransform;   // OpticSight   — for forward (downrange)

        // Anchor used for weapon-scale offset projection (lens / optic camera).
        private static Transform _reticleAnchor;
        private static Renderer  _lensRenderer;

        // The backLens child transform, used as the primary NDC anchor.
        // Its world position projected to viewport space gives the true screen-space
        // centre of the scope aperture — naturally tracking any weapon-scale shift,
        // housing offset, or camera/scope misalignment without a separate code path.
        private static Transform _backLensTransform;

        // CommandBuffer state
        private static CommandBuffer _cmdBuffer;
        private static Camera        _attachedCamera;
        private static bool          _preCullRegistered;

        // World-space TRS for the reticle quad (rebuilt in onPreCull)
        private static Matrix4x4 _reticleMatrix = Matrix4x4.identity;

        // Rendering state
        private static bool _settled;

        // When true, the reticle quad is positioned in world space and rendered
        // with cam.worldToCameraMatrix + cam.nonJitteredProjectionMatrix.
        // This shares the same float-precision path as the scope housing mesh,
        // so on big maps both jitter identically → no visible relative jitter.
        // Falls back to clip-space (identity matrices) when _backLensTransform
        // is unavailable.
        private static bool _worldSpaceMode;

        // Camera alignment state
        private static bool _alignmentActive;

        // Weapon-scale compensation offset (NDC space, -1..1).
        // When WeaponScalingPatch shrinks the ribcage, the scope housing shifts
        // on screen relative to where the camera-aligned center is.  This offset
        // moves the reticle to match that shift so reticle and housing stay locked.
        private static Vector2 _weaponScaleOffset;

        // ── Public API ───────────────────────────────────────────────────────

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
        /// Show the reticle.  Creates the mesh/material, attaches the CommandBuffer
        /// to the main camera, and registers the onPreCull hook.
        /// </summary>
        public static void Show(OpticSight os, float magnification = 1f)
        {
            if (!ScopeHousingMeshSurgeryPlugin.ShowReticle.Value) return;
            if (_savedMarkTex == null || os == null) return;

            try
            {
                _opticTransform = os.transform;

                // Primary NDC anchor: the backLens child transform.
                // Its world position is stable (survives mesh-killing by LensTransparency)
                // and projects to the true aperture centre after camera alignment.
                _backLensTransform = ScopeHierarchy.FindDeepChild(os.transform, "backLens")
                                  ?? ScopeHierarchy.FindDeepChild(os.transform, "backlens");

                // Fallback anchor chain: optic camera → lens renderer → optic root.
                _lensRenderer = os.LensRenderer;
                _reticleAnchor = _lensRenderer != null ? _lensRenderer.transform : null;
                if (_reticleAnchor == null) _reticleAnchor = _opticTransform;

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[Reticle] backLensTransform={((_backLensTransform != null) ? _backLensTransform.name : "not found")}; " +
                    $"fallback anchor={(_reticleAnchor != null ? _reticleAnchor.name : "null")}");
                EnsureMeshAndMaterial();

                _reticleMat.mainTexture = _savedMarkTex;
                ApplyHorizontalFlip();

                // Scale
                float configBase = ScopeHousingMeshSurgeryPlugin.ReticleBaseSize.Value;
                _baseScale = (configBase > 0f)
                    ? configBase
                    : ScopeHousingMeshSurgeryPlugin.CylinderRadius.Value * 2f;
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
            DetachFromCamera();
        }

        public static void Cleanup()
        {
            Hide();
            _savedMarkTex      = null;
            _savedMaskTex      = null;
            _opticTransform    = null;
            _backLensTransform = null;
            _reticleAnchor     = null;
            _lensRenderer      = null;
            _lastMag           = 1f;
            _baseScale         = 0f;
            _settled           = false;
            _worldSpaceMode    = false;
            _weaponScaleOffset = Vector2.zero;
        }

        /// <summary>
        /// Returns true if camera alignment is currently active while scoped.
        /// Used by ScopeEffectsRenderer to know that vignette/shadow can also
        /// render centered rather than tracking lens position.
        /// </summary>
        public static bool IsAlignmentActive => _alignmentActive && _settled;

        /// <summary>
        /// Current weapon-scale offset in NDC space (-1..1).
        /// ScopeEffectsRenderer reads this to shift vignette/shadow by the same amount
        /// so all overlays track the visually-shifted scope housing.
        /// </summary>
        public static Vector2 WeaponScaleOffset => _weaponScaleOffset;

        /// <summary>
        /// Returns the current optic transform (for ScopeEffectsRenderer to
        /// share camera alignment).
        /// </summary>
        public static Transform OpticTransform => _opticTransform;

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

            mainCam.AddCommandBuffer(CameraEvent.AfterEverything, _cmdBuffer);
            _attachedCamera = mainCam;

            if (!_preCullRegistered)
            {
                Camera.onPreCull += OnPreCullCallback;
                _preCullRegistered = true;
            }

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[Reticle] CommandBuffer attached to '{mainCam.name}' at AfterEverything");
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
                try { _attachedCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, _cmdBuffer); }
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

                // ── Weapon-scale offset ──────────────────────────────────────
                // The camera alignment above (cam.rotation = opticCam.rotation) already
                // makes the scope aperture appear at screen centre — zero offset is the
                // correct resting state and is jitter-free.
                //
                // The ONLY case that needs a non-zero offset: WeaponScalingPatch is
                // actively rescaling the weapon ribcage, which moves the scope housing
                // laterally in world space.  The camera still looks along the scope's
                // forward (rotation unchanged by scale), but the aperture has shifted
                // on screen.  We project the backLens world position to find that shift.
                //
                // We use _backLensTransform rather than _lensRenderer.bounds.center
                // because LensTransparency clears the mesh — after that, bounds.center
                // returns (0,0,0) and breaks the projection.  Transform.position is
                // unaffected by mesh operations.
                if (Patches.WeaponScalingPatch.IsScalingActive)
                {
                    // Best stable anchor: backLens (survives mesh-kill), then optic cam,
                    // then the cached lens anchor, then the optic root.
                    Transform anchor = (_backLensTransform as Transform)
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
            }

            RebuildMatrix(cam);
            RebuildCommandBuffer(cam);
        }

        // ── Reticle quad matrix ──────────────────────────────────────────────

        /// <summary>
        /// Build the reticle quad's transform matrix.
        ///
        /// Preferred path (world-space): place the quad at backLens.position in
        /// world space, oriented to face the camera, sized by FOV.  Both the
        /// housing mesh and the reticle go through cam.worldToCameraMatrix on
        /// the GPU, so they share the same float-precision errors on big maps
        /// and jitter identically → no visible relative jitter.
        ///
        /// Fallback (clip-space): when _backLensTransform is unavailable, draw
        /// a clip-space quad at screen center with an optional weapon-scale
        /// offset.  Stable but decoupled from the housing's precision path.
        /// </summary>
        private static void RebuildMatrix(Camera cam)
        {
            if (cam == null) return;

            float mag = Mathf.Max(1f, _lastMag);
            float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
            float tanHalfFov = Mathf.Max(0.01f, Mathf.Tan(fovRad * 0.5f));
            const float referenceLensDistance = 0.075f;

            float angularSize = (_baseScale / mag) / referenceLensDistance;
            float ndcSize = angularSize / tanHalfFov;
            ndcSize = Mathf.Clamp(ndcSize, 0.01f, 2f);

            if (_backLensTransform != null)
            {
                // ── World-space mode ──────────────────────────────────────
                Vector3 worldPos = _backLensTransform.position;
                float dist = Vector3.Distance(cam.transform.position, worldPos);
                dist = Mathf.Max(0.05f, dist);

                // Convert ndcSize (clip-space fraction) to a world-space size
                // at the back-lens distance.
                //   clip height range = 2.0  →  ndcSize covers ndcSize/2 of screen.
                //   screen height at dist d  = 2 * d * tan(fovV/2).
                //   world size = ndcSize * d * tan(fovV/2).
                // Equal X/Y: the projection matrix handles aspect correction.
                float worldSize = ndcSize * dist * tanHalfFov;

                _reticleMatrix = Matrix4x4.TRS(
                    worldPos,
                    cam.transform.rotation,
                    new Vector3(worldSize, worldSize, 1f));

                _worldSpaceMode = true;
            }
            else
            {
                // ── Clip-space fallback ───────────────────────────────────
                float aspect = GetDisplayAspect(cam);
                Vector3 pos   = new Vector3(_weaponScaleOffset.x, _weaponScaleOffset.y, 0.5f);
                Vector3 scale = new Vector3(ndcSize / Mathf.Max(0.01f, aspect), ndcSize, 1f);
                _reticleMatrix = Matrix4x4.TRS(pos, Quaternion.identity, scale);

                _worldSpaceMode = false;
            }
        }

        /// <summary>
        /// Rebuild the CommandBuffer.
        ///
        /// World-space mode:  worldToCameraMatrix + nonJitteredProjectionMatrix.
        ///   The reticle goes through the same view transform as the scope housing
        ///   → identical float-precision errors on big maps → locked together.
        ///   nonJitteredProjectionMatrix avoids TAA sub-pixel edge flickering
        ///   (the reticle draws at AfterEverything, outside the TAA pipeline).
        ///
        /// Clip-space fallback:  identity view + identity projection.
        /// </summary>
        private static void RebuildCommandBuffer(Camera cam)
        {
            _cmdBuffer.Clear();

            // ── DLSS/FSR viewport fix ─────────────────────────────────────
            _cmdBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            _cmdBuffer.SetViewport(GetDisplayViewport(cam));

            if (_worldSpaceMode)
            {
                _cmdBuffer.SetViewProjectionMatrices(
                    cam.worldToCameraMatrix,
                    cam.nonJitteredProjectionMatrix);
            }
            else
            {
                _cmdBuffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            }

            _cmdBuffer.DrawMesh(_reticleMesh, _reticleMatrix, _reticleMat, 0, -1);

            // Restore original matrices for any subsequent rendering.
            _cmdBuffer.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
        }

        // ── Private helpers ─────────────────────────────────────────────────

        private static Rect GetDisplayViewport(Camera cam)
        {
            float w = Mathf.Max(1f, Screen.width);
            float h = Mathf.Max(1f, Screen.height);

            // Under DLSS/FSR, camera pixelRect can reflect the lower-resolution
            // internal render size, which would pin overlays to lower-left when
            // used as the viewport. Prefer display-space dimensions instead.
            if (cam != null)
            {
                w = Mathf.Max(w, cam.pixelWidth);
                h = Mathf.Max(h, cam.pixelHeight);
            }

            return new Rect(0f, 0f, w, h);
        }

        private static float GetDisplayAspect(Camera cam)
        {
            Rect r = GetDisplayViewport(cam);
            return Mathf.Max(0.01f, r.width / Mathf.Max(1f, r.height));
        }

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
                Shader alphaShader =
                    Shader.Find("Sprites/Default")          ??
                    Shader.Find("UI/Default")               ??
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
                _reticleMat.SetInt("_ZTest", (int)CompareFunction.Always);
                _reticleMat.SetInt("_ZWrite", 0);

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[Reticle] Created material (shader='{(alphaShader != null ? alphaShader.name : "null")}')");
            }

            ApplyHorizontalFlip();
        }
    }
}
