using EFT.CameraControl;
using UnityEngine;
using UnityEngine.Rendering;

namespace PiPDisabler
{
    /// <summary>
    /// Renders scope vignette and shadow effects via a CommandBuffer injected at
    /// CameraEvent.AfterForwardAlpha on the main FPS camera.
    ///
    /// The shadow shares the same render stage as ReticleRenderer so both overlays
    /// see the same scene viewport, depth, and stencil data.
    ///
    /// ── VIGNETTE ───────────────────────────────────────────────────────────────
    /// Screen-space quad centred in view with fixed on-screen size.
    /// Circular feather: transparent centre → smooth darkening at aperture,
    /// then softly fades back out so the overlay never reads as a rectangle.
    ///
    /// ── SHADOW ─────────────────────────────────────────────────────────────────
    /// Screen-filling quad in front of the camera.  Circular hole in the centre
    /// with aspect-ratio correction so it appears round on non-square viewports.
    ///
    /// Both textures are generated at runtime and only rebuilt when config changes.
    /// </summary>
    internal static class ScopeEffectsRenderer
    {
        // ── Vignette ────────────────────────────────────────────────────────
        private static Mesh       _vigMesh;
        private static Material   _vigMat;
        private static Texture2D  _vigTex;
        private static float      _lastVigSoftness  = -1f;
        private static float      _lastVigOpacity   = -1f;
        private static float      _lastVigSizeMult  = -1f;
        private static float      _lastVigAspect    = -1f;
        private static Matrix4x4  _vigMatrix = Matrix4x4.identity;
        private static bool       _vigActive;

        // ── Shadow ──────────────────────────────────────────────────────────
        private static Mesh       _shadowMesh;
        private static Material   _shadowMat;
        private static Texture2D  _shadowTex;
        private static float      _lastShadowRadius   = -1f;
        private static float      _lastShadowSoftness = -1f;
        private static float      _lastShadowOpacity  = -1f;
        private static float      _lastShadowAspect   = -1f;
        private static int        _lastShadowTexWidth = -1;
        private static int        _lastShadowTexHeight = -1;
        private static Matrix4x4  _shadowMatrix = Matrix4x4.identity;
        private static bool       _shadowActive;
        private static bool       _effectsVisible;
        private static bool       _persistShadowUntilFovRestore;

        // ── Shared stencil debug ───────────────────────────────────────────
        private static Material   _stencilDebugMat;
        private static bool       _hasStencilSupport;

        // ── CommandBuffer ───────────────────────────────────────────────────
        private static CommandBuffer _cmdBuffer;
        private static Camera        _attachedCamera;
        private static CameraEvent   _attachedEvent = CameraEvent.AfterForwardAlpha;
        private static bool          _preCullRegistered;

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        public static void Show()
        {
            EnsureStencilDebugMaterial();

            if (PiPDisablerPlugin.VignetteEnabled.Value)
            {
                EnsureVignetteMeshAndMat();
                RefreshVignetteTexture();
                _vigActive = true;
            }
            else
            {
                _vigActive = false;
            }

            if (PiPDisablerPlugin.ScopeShadowEnabled.Value)
            {
                EnsureShadowMeshAndMat();
                RefreshShadowTexture();
                _shadowActive = true;
            }
            else
            {
                _shadowActive = false;
            }

            _effectsVisible = true;
            _persistShadowUntilFovRestore = false;

            // Force re-attach to guarantee this CommandBuffer is ordered AFTER
            // ReticleRenderer's CB. If we just call AttachToCamera() it returns
            // early when already attached to the same camera, leaving the CB at
            // its stale position — which ends up before ReticleRenderer's CB
            // whenever Reticle detaches/re-attaches (e.g. after persist-on-unscope
            // + optic mode switch, or after a magnification mode switch while
            // scoped). The shadow reads stencil written by ReticleRenderer, so
            // it must execute AFTER that pass.
            ReattachAfterReticle();

            PiPDisablerPlugin.LogInfo(
                $"[ScopeEffects] Showing: vignette={_vigActive} shadow={_shadowActive} (CommandBuffer)");
        }

        /// <summary>Per-frame update — call from ScopeLifecycle.Tick().</summary>
        public static void UpdateTransform()
        {
            EnsureCorrectCameraEvent();

            // Refresh textures if config changed
            if (_vigActive)  RefreshVignetteTexture();
            if (_shadowActive) RefreshShadowTexture();

            // Re-attach if camera changed
            var mainCam = Helpers.GetMainCamera();
            if (mainCam != null && mainCam != _attachedCamera)
                AttachToCamera();
        }

        public static void Hide()
        {
            _effectsVisible = false;
            _persistShadowUntilFovRestore = false;

            // Keep allocated resources and camera hook alive so returning to ADS
            // does not force expensive re-attachment/rebuild work in the same frame.
            _cmdBuffer?.Clear();
        }

        public static bool OnScopeExit(bool allowShadowPersist)
        {
            _vigActive = false;

            bool keepShadow =
                allowShadowPersist &&
                PiPDisablerPlugin.ModEnabled.Value &&
                PiPDisablerPlugin.ScopeShadowEnabled.Value &&
                PiPDisablerPlugin.ScopeShadowPersistOnUnscope.Value &&
                _shadowActive;

            if (keepShadow)
            {
                _effectsVisible = true;
                _persistShadowUntilFovRestore = true;
                AttachToCamera();
                return true;
            }

            Cleanup();
            return false;
        }

        public static void Cleanup()
        {
            _effectsVisible = false;
            _vigActive = false;
            _shadowActive = false;
            _persistShadowUntilFovRestore = false;
            DetachFromCamera();
        }

        // ─────────────────────────────────────────────────────────────────────
        // CommandBuffer management
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Remove and re-add the CommandBuffer so it sits AFTER ReticleRenderer's CB
        /// in the camera's execution list.  Must be called after ReticleRenderer.Show()
        /// has (re-)attached its own CB.
        /// </summary>
        private static void ReattachAfterReticle()
        {
            var mainCam = Helpers.GetMainCamera();
            if (mainCam == null) return;

            if (_attachedCamera != null && _attachedCamera != mainCam)
                DetachFromCamera();

            CameraEvent targetEvent = ReticleRenderer.CurrentCameraEvent;

            if (_attachedCamera == mainCam && _cmdBuffer != null)
            {
                // Already on the correct camera — remove and re-add to push this CB
                // to the end of the list (i.e. after ReticleRenderer's CB).
                try { mainCam.RemoveCommandBuffer(_attachedEvent, _cmdBuffer); }
                catch (System.Exception) { }
                mainCam.AddCommandBuffer(targetEvent, _cmdBuffer);
                _attachedEvent = targetEvent;

                PiPDisablerPlugin.LogVerbose(
                    $"[ScopeEffects] CommandBuffer reordered on '{mainCam.name}' at {_attachedEvent}");
                return;
            }

            // First-time attachment (same path as before).
            if (_cmdBuffer == null)
                _cmdBuffer = new CommandBuffer { name = "ScopeEffectsOverlay" };

            mainCam.AddCommandBuffer(targetEvent, _cmdBuffer);
            _attachedCamera = mainCam;
            _attachedEvent = targetEvent;

            if (!_preCullRegistered)
            {
                Camera.onPreCull += OnPreCullCallback;
                _preCullRegistered = true;
            }

            PiPDisablerPlugin.LogVerbose(
                $"[ScopeEffects] CommandBuffer attached to '{mainCam.name}' at {_attachedEvent}");
        }

        private static void AttachToCamera()
        {
            var mainCam = Helpers.GetMainCamera();
            if (mainCam == null) return;

            if (_attachedCamera != null && _attachedCamera != mainCam)
                DetachFromCamera();

            if (_attachedCamera == mainCam) return;

            if (_cmdBuffer == null)
                _cmdBuffer = new CommandBuffer { name = "ScopeEffectsOverlay" };

            CameraEvent targetEvent = ReticleRenderer.CurrentCameraEvent;
            mainCam.AddCommandBuffer(targetEvent, _cmdBuffer);
            _attachedCamera = mainCam;
            _attachedEvent = targetEvent;

            if (!_preCullRegistered)
            {
                Camera.onPreCull += OnPreCullCallback;
                _preCullRegistered = true;
            }

            PiPDisablerPlugin.LogVerbose(
                $"[ScopeEffects] CommandBuffer attached to '{mainCam.name}' at {_attachedEvent}");
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

        private static void EnsureCorrectCameraEvent()
        {
            if (_attachedCamera == null || _cmdBuffer == null) return;

            CameraEvent desiredEvent = ReticleRenderer.CurrentCameraEvent;
            if (desiredEvent == _attachedEvent) return;

            try { _attachedCamera.RemoveCommandBuffer(_attachedEvent, _cmdBuffer); }
            catch (System.Exception) { }

            _attachedCamera.AddCommandBuffer(desiredEvent, _cmdBuffer);
            _attachedEvent = desiredEvent;
        }

        // ─────────────────────────────────────────────────────────────────────
        // onPreCull — rebuild CommandBuffer with final-pose data
        // ─────────────────────────────────────────────────────────────────────

        private static void OnPreCullCallback(Camera cam)
        {
            if (cam != _attachedCamera) return;
            if (_cmdBuffer == null) return;

            if (!_effectsVisible)
            {
                _cmdBuffer.Clear();
                return;
            }

            if (_persistShadowUntilFovRestore && !ShouldKeepPersistedShadowVisible())
            {
                Cleanup();
                ReticleRenderer.StopStencilOnlyPersistence();
                return;
            }

            if (!_vigActive && !_shadowActive)
            {
                _cmdBuffer.Clear();
                return;
            }

            // Rebuild matrices in pure screen-space
            if (_vigActive)
                RebuildVignetteMatrix(cam);
            if (_shadowActive)
                RebuildShadowMatrix(cam);

            RebuildCommandBuffer(cam);
        }

        private static void RebuildVignetteMatrix(Camera cam)
        {
            if (cam == null) return;

            // Clip-space centered overlay, independent of world/lens transforms.
            float ndcScale = GetFixedOverlayScale();

            _vigMatrix = Matrix4x4.TRS(
                new Vector3(0f, 0f, 0.6f),
                Quaternion.identity,
                new Vector3(ndcScale, ndcScale, 1f));
        }

        private static void RebuildShadowMatrix(Camera cam)
        {
            if (cam == null) return;

            // Clip-space centered overlay, independent of world/lens transforms.
            // A quad scale of 2 fills the entire viewport in clip-space.
            float ndcScale = Mathf.Max(2.25f, GetFixedOverlayScale());

            _shadowMatrix = Matrix4x4.TRS(
                new Vector3(0f, 0f, 0.7f),
                Quaternion.identity,
                new Vector3(ndcScale, ndcScale, 1f));
        }

        private static void RebuildCommandBuffer(Camera cam)
        {
            _cmdBuffer.Clear();
            bool isAfterEverything = _attachedEvent == CameraEvent.AfterEverything;

            if (isAfterEverything)
            {
                _cmdBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                _cmdBuffer.SetViewport(GetDisplayViewport(cam));
            }
            else
            {
                _cmdBuffer.SetViewport(GetSceneViewport(cam));
            }

            bool useStencil = _hasStencilSupport;

            // Pure screen-space draw (clip-space matrices).
            _cmdBuffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);

            if (useStencil)
            {
                var fullScreenMatrix = Matrix4x4.TRS(
                    Vector3.zero, Quaternion.identity, new Vector3(2f, 2f, 1f));

                // Consume the stencil ReticleRenderer already wrote this frame.
                if (PiPDisablerPlugin.DebugShowScopeShadowMask.Value && _stencilDebugMat != null)
                    _cmdBuffer.DrawMesh(_shadowMesh, fullScreenMatrix, _stencilDebugMat, 0, -1);
            }

            // Draw shadow first (behind vignette in render order)
            if (_shadowActive && _shadowMat != null && _shadowMesh != null)
                _cmdBuffer.DrawMesh(_shadowMesh, _shadowMatrix, _shadowMat, 0, -1);

            // Draw vignette only in lens mask area
            if (_vigActive && useStencil && _vigMat != null && _vigMesh != null)
                _cmdBuffer.DrawMesh(_vigMesh, _vigMatrix, _vigMat, 0, -1);

            // Restore original matrices
            _cmdBuffer.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Vignette texture + mesh/material
        // ─────────────────────────────────────────────────────────────────────

        private static void EnsureVignetteMeshAndMat()
        {
            if (_vigMesh == null)
                _vigMesh = BuildQuadMesh("VignetteQuad");

            if (_vigMat == null)
            {
                Shader shader = _hasStencilSupport
                    ? Shader.Find("UI/Default")
                    : FindAlphaShader();

                _vigMat = new Material(shader)
                {
                    color       = Color.white,
                    renderQueue = 3099
                };
                _vigMat.SetInt("_ZTest", (int)CompareFunction.Always);
                _vigMat.SetInt("_ZWrite", 0);
                if (_hasStencilSupport)
                {
                    _vigMat.SetFloat("_Stencil", 1f);
                    _vigMat.SetFloat("_StencilComp", (float)CompareFunction.Equal);
                    _vigMat.SetFloat("_StencilOp", (float)StencilOp.Keep);
                    _vigMat.SetFloat("_StencilReadMask", 255f);
                    _vigMat.SetFloat("_StencilWriteMask", 0f);
                }
            }
        }

        /// <summary>
        /// Circular dual-feather with aspect-ratio correction: transparent centre →
        /// darkened aperture edge → soft fade-out before texture borders.
        /// Now screen-filling (like shadow), so X distances are stretched by
        /// the aspect ratio to keep the circle round.
        /// </summary>
        private static void RefreshVignetteTexture()
        {
            float soft = PiPDisablerPlugin.VignetteSoftness.Value;
            float opac = PiPDisablerPlugin.VignetteOpacity.Value;
            float mult = PiPDisablerPlugin.VignetteSizeMult.Value;

            var cam = Helpers.GetMainCamera();
            float aspect = GetActiveAspect(cam);

            if (Mathf.Abs(soft - _lastVigSoftness) < 0.001f &&
                Mathf.Abs(opac - _lastVigOpacity)  < 0.005f &&
                Mathf.Abs(mult - _lastVigSizeMult) < 0.001f &&
                Mathf.Abs(aspect - _lastVigAspect) < 0.01f) return;
            _lastVigSoftness = soft;
            _lastVigOpacity  = opac;
            _lastVigSizeMult = mult;
            _lastVigAspect   = aspect;

            const int S = 256;
            if (_vigTex == null)
            {
                _vigTex            = new Texture2D(S, S, TextureFormat.RGBA32, false);
                _vigTex.name       = "ScopeLensVignetteTex";
                _vigTex.wrapMode   = TextureWrapMode.Clamp;
                _vigTex.filterMode = FilterMode.Bilinear;
            }

            // VignetteSizeMult controls where the aperture edge sits.
            // mult=1 → edge near a circle inscribed in height.
            // mult<1 → edge moves inward (more visible darkening).
            // mult>1 → edge moves outward (less visible darkening).
            float edgeR = mult;

            // Use an inward feather (clear center -> dark edge) and an outward
            // feather (dark edge -> clear corners) so blending feels natural
            // without leaving a full-screen dark rectangle.
            float innerR = edgeR * Mathf.Clamp01(1f - soft);
            float outerR = edgeR + Mathf.Max(0.02f, soft * 0.65f);

            var pixels = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // Aspect-corrected distance from center (normalized -1..1)
                float nx   = ((float)x / S - 0.5f) * 2f * aspect;
                float ny   = ((float)y / S - 0.5f) * 2f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);

                float inward = Mathf.Clamp01((dist - innerR) / Mathf.Max(0.001f, edgeR - innerR));
                float outward = 1f - Mathf.Clamp01((dist - edgeR) / Mathf.Max(0.001f, outerR - edgeR));

                // Bell-shaped alpha profile that peaks at the aperture edge.
                float feather = Mathf.SmoothStep(0f, 1f, inward) * Mathf.SmoothStep(0f, 1f, outward);
                feather = Mathf.Pow(feather, 1.2f);

                byte a = (byte)(feather * opac * 255f);
                pixels[y * S + x] = new Color32(0, 0, 0, a);
            }

            _vigTex.SetPixels32(pixels);
            _vigTex.Apply();
            if (_vigMat != null) _vigMat.mainTexture = _vigTex;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Shadow texture + mesh/material
        // ─────────────────────────────────────────────────────────────────────

        private static void EnsureShadowMeshAndMat()
        {
            if (_shadowMesh == null)
                _shadowMesh = BuildQuadMesh("ShadowQuad");

            if (_shadowMat == null)
            {
                Shader shader = _hasStencilSupport
                    ? Shader.Find("UI/Default")
                    : FindAlphaShader();

                _shadowMat = new Material(shader)
                {
                    color       = Color.white,
                    renderQueue = 3050
                };
                _shadowMat.SetInt("_ZTest", (int)CompareFunction.Always);
                _shadowMat.SetInt("_ZWrite", 0);
                if (_hasStencilSupport)
                {
                    _shadowMat.SetFloat("_Stencil", 1f);
                    _shadowMat.SetFloat("_StencilComp", (float)CompareFunction.NotEqual);
                    _shadowMat.SetFloat("_StencilOp", (float)StencilOp.Keep);
                    _shadowMat.SetFloat("_StencilReadMask", 255f);
                    _shadowMat.SetFloat("_StencilWriteMask", 0f);
                }
            }
        }

        /// <summary>
        /// Aspect-ratio corrected shadow texture.  X distances are stretched by
        /// the aspect ratio so the circle appears round on non-square viewports.
        /// </summary>
        private static void RefreshShadowTexture()
        {
            float radius = PiPDisablerPlugin.ScopeShadowRadius.Value;
            float soft   = PiPDisablerPlugin.ScopeShadowSoftness.Value;
            float opac   = PiPDisablerPlugin.ScopeShadowOpacity.Value;

            var cam = Helpers.GetMainCamera();
            Rect viewport = GetActiveViewport(cam);
            int texW = Mathf.Clamp(Mathf.RoundToInt(viewport.width), 64, 4096);
            int texH = Mathf.Clamp(Mathf.RoundToInt(viewport.height), 64, 4096);
            float aspect = texW / Mathf.Max(1f, (float)texH);

            bool sizeChanged = texW != _lastShadowTexWidth || texH != _lastShadowTexHeight;
            if (!sizeChanged &&
                Mathf.Abs(radius - _lastShadowRadius)   < 0.001f &&
                Mathf.Abs(soft   - _lastShadowSoftness) < 0.001f &&
                Mathf.Abs(opac   - _lastShadowOpacity)  < 0.005f &&
                Mathf.Abs(aspect - _lastShadowAspect)   < 0.01f) return;
            _lastShadowRadius    = radius;
            _lastShadowSoftness  = soft;
            _lastShadowOpacity   = opac;
            _lastShadowAspect    = aspect;
            _lastShadowTexWidth  = texW;
            _lastShadowTexHeight = texH;

            if (_shadowTex == null || _shadowTex.width != texW || _shadowTex.height != texH)
            {
                _shadowTex            = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
                _shadowTex.name       = "ScopeShadowTex";
                _shadowTex.wrapMode   = TextureWrapMode.Clamp;
                _shadowTex.filterMode = FilterMode.Bilinear;
            }

            float innerR = radius * 2f;
            float softR  = soft * 2f;
            float outerR = innerR + softR;
            var   pixels = new Color32[texW * texH];

            for (int y = 0; y < texH; y++)
            for (int x = 0; x < texW; x++)
            {
                float nx   = ((float)x / texW - 0.5f) * 2f * aspect;
                float ny   = ((float)y / texH - 0.5f) * 2f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                float t    = Mathf.Clamp01((dist - innerR) / Mathf.Max(0.01f, outerR - innerR));
                byte  a    = (byte)(Mathf.SmoothStep(0f, 1f, t) * opac * 255f);
                pixels[y * texW + x] = new Color32(0, 0, 0, a);
            }

            _shadowTex.SetPixels32(pixels);
            _shadowTex.Apply();
            if (_shadowMat != null) _shadowMat.mainTexture = _shadowTex;

            PiPDisablerPlugin.LogVerbose(
                $"[ScopeEffects] Shadow texture rebuilt: {texW}x{texH} aspect={aspect:F2} radius={radius} soft={soft}");
        }

        private static Rect GetDisplayViewport(Camera cam)
            => Helpers.GetDisplayViewport(cam);

        private static Rect GetSceneViewport(Camera cam)
        {
            return new Rect(0f, 0f,
                Mathf.Max(1f, cam.pixelWidth),
                Mathf.Max(1f, cam.pixelHeight));
        }

        private static Rect GetActiveViewport(Camera cam)
        {
            return _attachedEvent == CameraEvent.AfterEverything
                ? GetDisplayViewport(cam)
                : GetSceneViewport(cam);
        }

        private static float GetActiveAspect(Camera cam)
        {
            Rect r = GetActiveViewport(cam);
            return Mathf.Max(0.01f, r.width / Mathf.Max(1f, r.height));
        }

        private static float GetFixedOverlayScale()
        {
            // Keep vignette/shadow overlays at a fixed visual size regardless of
            // camera FOV or optic magnification.
            return 3.2f;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Shared helpers
        // ─────────────────────────────────────────────────────────────────────

        private static Shader FindAlphaShader() =>
            Shader.Find("Sprites/Default")         ??
            Shader.Find("UI/Default")              ??
            Shader.Find("Unlit/Transparent")       ??
            Shader.Find("Particles/Alpha Blended") ??
            Shader.Find("Legacy Shaders/Transparent/Diffuse");

        private static bool ShouldKeepPersistedShadowVisible()
        {
            if (!_persistShadowUntilFovRestore) return false;
            if (!PiPDisablerPlugin.ModEnabled.Value) return false;
            if (!PiPDisablerPlugin.ScopeShadowEnabled.Value) return false;

            bool hasActiveOptic = false;
            float currentFov = FovController.ZoomBaselineFov;

            if (CameraClass.Exist && CameraClass.Instance != null)
            {
                hasActiveOptic = CameraClass.Instance.OpticCameraManager != null &&
                                 CameraClass.Instance.OpticCameraManager.CurrentOpticSight != null;
                currentFov = CameraClass.Instance.Camera != null
                    ? CameraClass.Instance.Camera.fieldOfView
                    : CameraClass.Instance.Fov;
            }

            return hasActiveOptic || currentFov < FovController.ZoomBaselineFov;
        }

        private static void EnsureStencilDebugMaterial()
        {
            if (_stencilDebugMat != null) return;

            Shader stencilShader = Shader.Find("UI/Default");
            _hasStencilSupport = stencilShader != null;
            if (!_hasStencilSupport) return;

            _stencilDebugMat = new Material(stencilShader)
            {
                color = new Color(0.1f, 1f, 0.1f, 0.45f),
                renderQueue = 5000
            };
            _stencilDebugMat.SetFloat("_Stencil", 0f);
            _stencilDebugMat.SetFloat("_StencilComp", (float)CompareFunction.NotEqual);
            _stencilDebugMat.SetFloat("_StencilOp", (float)StencilOp.Keep);
            _stencilDebugMat.SetFloat("_StencilReadMask", 255f);
            _stencilDebugMat.SetInt("_ZTest", (int)CompareFunction.Always);
            _stencilDebugMat.SetInt("_ZWrite", 0);
        }

        private static Mesh BuildQuadMesh(string meshName)
        {
            var mesh = new Mesh { name = meshName };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f), new Vector3(-0.5f,  0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1,  0, 3, 2 };
            mesh.normals = new[]
            {
                -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
