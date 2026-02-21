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
        private static Transform _lensTransform;    // LensRenderer — for position / distance
        private static Transform _opticTransform;   // OpticSight   — for forward (downrange)

        // CommandBuffer state
        private static CommandBuffer _cmdBuffer;
        private static Camera        _attachedCamera;
        private static bool          _preCullRegistered;

        // World-space TRS for the reticle quad (rebuilt in onPreCull)
        private static Matrix4x4 _reticleMatrix = Matrix4x4.identity;

        // ADS-in settled detection (position-based, proven in v4.5.4)
        private static Vector3 _prevLensPos;
        private static bool    _settled;
        private static int     _settledFrameCount;
        private const  int     SETTLED_FRAMES_REQUIRED = 3;

        // Debug telemetry throttling
        private static int _lastDiagLogFrame = -1;
        private static float _lastLensDelta;

        // Fixed render distance for the centered quad
        private const float RENDER_DISTANCE = 0.3f;

        // Camera alignment state
        private static bool _alignmentActive;
        private static Quaternion _smoothedCameraRotation = Quaternion.identity;
        private static bool _hasSmoothedRotation;

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
                _lensTransform = os.LensRenderer != null
                    ? os.LensRenderer.transform
                    : os.transform;
                _opticTransform = os.transform;

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

                // Only reset settled detection on fresh ADS-in (not already settled).
                // Magnification switches and reticle mode changes call Show() again
                // while already scoped — resetting would cause a 3-frame flicker.
                if (!_settled)
                {
                    _settledFrameCount = 0;
                    _prevLensPos = _lensTransform != null ? _lensTransform.position : Vector3.zero;
                }
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
            if (_cmdBuffer == null || _lensTransform == null) return;

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
            _settledFrameCount = 0;
            _hasSmoothedRotation = false;
            DetachFromCamera();
        }

        public static void Cleanup()
        {
            Hide();
            _savedMarkTex      = null;
            _savedMaskTex      = null;
            _lensTransform     = null;
            _opticTransform    = null;
            _lastMag           = 1f;
            _baseScale         = 0f;
            _settled           = false;
            _settledFrameCount = 0;
            _hasSmoothedRotation = false;
        }

        /// <summary>
        /// Returns true if camera alignment is currently active (scoped + settled).
        /// Used by ScopeEffectsRenderer to know that vignette/shadow can also
        /// render centered rather than tracking lens position.
        /// </summary>
        public static bool IsAlignmentActive => _alignmentActive && _settled;

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
            if (_cmdBuffer == null || _lensTransform == null || _reticleMat == null) return;

            bool diagEnabled = ScopeHousingMeshSurgeryPlugin.JitterDiagnostics != null
                && ScopeHousingMeshSurgeryPlugin.JitterDiagnostics.Value;
            int diagInterval = ScopeHousingMeshSurgeryPlugin.JitterDiagnosticsIntervalFrames != null
                ? Mathf.Max(1, ScopeHousingMeshSurgeryPlugin.JitterDiagnosticsIntervalFrames.Value)
                : 30;

            // ── Settled detection (position-based) ────────────────────────
            if (!_settled)
            {
                Vector3 pos = _lensTransform.position;
                float delta = (pos - _prevLensPos).sqrMagnitude;
                _lastLensDelta = Mathf.Sqrt(delta);
                _prevLensPos = pos;

                float thresh = ScopeHousingMeshSurgeryPlugin.AdsSettledThreshold.Value;
                float threshSq = thresh * thresh;

                if (delta < threshSq)
                {
                    _settledFrameCount++;
                    if (_settledFrameCount >= SETTLED_FRAMES_REQUIRED)
                    {
                        _settled = true;
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            "[Reticle] Lens settled — starting render");
                    }
                }
                else
                {
                    _settledFrameCount = 0;
                }

                if (!_settled)
                {
                    _cmdBuffer.Clear();
                    return;
                }
            }

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
            float alignmentAngle = -1f;
            if (_alignmentActive)
            {
                Transform opticCamTf = PiPDisabler.OpticCameraTransform;
                if (opticCamTf != null)
                {
                    Quaternion targetRotation = opticCamTf.rotation;
                    if (!_hasSmoothedRotation)
                    {
                        _smoothedCameraRotation = targetRotation;
                        _hasSmoothedRotation = true;
                    }

                    alignmentAngle = Quaternion.Angle(_smoothedCameraRotation, targetRotation);
                    float deadzone = ScopeHousingMeshSurgeryPlugin.CameraAlignmentDeadzoneDegrees != null
                        ? Mathf.Max(0f, ScopeHousingMeshSurgeryPlugin.CameraAlignmentDeadzoneDegrees.Value)
                        : 0f;

                    if (alignmentAngle > deadzone)
                    {
                        float smooth = ScopeHousingMeshSurgeryPlugin.CameraAlignmentSmoothing != null
                            ? Mathf.Clamp01(ScopeHousingMeshSurgeryPlugin.CameraAlignmentSmoothing.Value)
                            : 0f;
                        if (smooth <= 0f)
                            _smoothedCameraRotation = targetRotation;
                        else
                            _smoothedCameraRotation = Quaternion.Slerp(_smoothedCameraRotation, targetRotation, smooth);
                    }

                    cam.transform.rotation = _smoothedCameraRotation;
                }
            }

            if (diagEnabled && Time.frameCount - _lastDiagLogFrame >= diagInterval)
            {
                Vector3 lensPos = _lensTransform != null ? _lensTransform.position : Vector3.zero;
                float lensDelta = _lastLensDelta;
                Transform opticCamTf = PiPDisabler.OpticCameraTransform;
                float camOpticAngle = opticCamTf != null
                    ? Vector3.Angle(cam.transform.forward, opticCamTf.forward)
                    : -1f;
                float deadzone = ScopeHousingMeshSurgeryPlugin.CameraAlignmentDeadzoneDegrees != null
                    ? Mathf.Max(0f, ScopeHousingMeshSurgeryPlugin.CameraAlignmentDeadzoneDegrees.Value)
                    : 0f;
                float smoothing = ScopeHousingMeshSurgeryPlugin.CameraAlignmentSmoothing != null
                    ? Mathf.Clamp01(ScopeHousingMeshSurgeryPlugin.CameraAlignmentSmoothing.Value)
                    : 0f;

                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[JitterDiag][Reticle] scene={ScopeHousingMeshSurgeryPlugin.GetActiveSceneNameSafe()} " +
                    $"frame={Time.frameCount} cam='{cam.name}' fov={cam.fieldOfView:F2} settled={_settled} " +
                    $"settledCount={_settledFrameCount}/{SETTLED_FRAMES_REQUIRED} thresh={ScopeHousingMeshSurgeryPlugin.AdsSettledThreshold.Value:F6} " +
                    $"lensDelta={lensDelta:F6} mag={_lastMag:F2} base={_baseScale:F4} " +
                    $"opticCam={(opticCamTf != null ? opticCamTf.name : "null")} camOpticAngle={camOpticAngle:F4} " +
                    $"alignDelta={alignmentAngle:F4} deadzone={deadzone:F4} smooth={smoothing:F2}");

                _lastDiagLogFrame = Time.frameCount;
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
        /// Size is computed to match the visual angle the reticle would
        /// subtend at the real lens distance.
        /// </summary>
        private static void RebuildMatrix(Camera cam)
        {
            if (cam == null) return;

            Transform camTf = cam.transform;

            // Position: fixed distance along camera forward (= screen center)
            Vector3 worldPos = camTf.position + camTf.forward * RENDER_DISTANCE;

            // Billboard: face the camera
            Quaternion rot = Quaternion.LookRotation(camTf.forward, camTf.up);

            // Size: match visual angle of (baseScale/mag) at real lens distance,
            // but drawn at RENDER_DISTANCE.
            float realDist = _lensTransform != null
                ? Vector3.Distance(camTf.position, _lensTransform.position)
                : RENDER_DISTANCE;
            if (realDist < 0.01f) realDist = RENDER_DISTANCE;

            float mag = Mathf.Max(1f, _lastMag);
            float worldSize = (_baseScale / mag) / realDist * RENDER_DISTANCE;

            Vector3 scale = new Vector3(worldSize, worldSize, worldSize);

            _reticleMatrix = Matrix4x4.TRS(worldPos, rot, scale);
        }

        /// <summary>
        /// Rebuild the CommandBuffer.  Standard world-space rendering with
        /// non-jittered projection — same proven path as ScopeEffectsRenderer.
        /// </summary>
        private static void RebuildCommandBuffer(Camera cam)
        {
            _cmdBuffer.Clear();

            // ── DLSS/FSR viewport fix ─────────────────────────────────────
            _cmdBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            _cmdBuffer.SetViewport(new Rect(0, 0, Screen.width, Screen.height));

            // ── Non-jittered projection ───────────────────────────────────
            Matrix4x4 viewMatrix = cam.worldToCameraMatrix;
            Matrix4x4 projMatrix = cam.nonJitteredProjectionMatrix;

            _cmdBuffer.SetViewProjectionMatrices(viewMatrix, projMatrix);

            _cmdBuffer.DrawMesh(_reticleMesh, _reticleMatrix, _reticleMat, 0, -1);

            // Restore original matrices
            _cmdBuffer.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
        }

        // ── Private helpers ─────────────────────────────────────────────────

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

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[Reticle] Created material (shader='{(alphaShader != null ? alphaShader.name : "null")}')");
            }

            ApplyHorizontalFlip();
        }
    }
}
