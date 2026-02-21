using UnityEngine;
using UnityEngine.Rendering;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Renders scope vignette and shadow effects via a CommandBuffer injected at
    /// CameraEvent.AfterEverything on Camera.main, using nonJitteredProjectionMatrix.
    ///
    /// This is the same technique as ReticleRenderer — both effects are drawn after
    /// all post-processing (TAA, DLSS, FSR) with explicitly non-jittered matrices,
    /// eliminating edge flickering from TAA's jittered projection.
    ///
    /// ── VIGNETTE ───────────────────────────────────────────────────────────────
    /// Screen-space quad centred in view, scaled by FOV.
    /// Circular feather: transparent centre → smooth darkening toward edge,
    /// then holds darkness outside the aperture to blend with scope housing.
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

        // ── CommandBuffer ───────────────────────────────────────────────────
        private static CommandBuffer _cmdBuffer;
        private static Camera        _attachedCamera;
        private static bool          _preCullRegistered;

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        public static void Show(Transform lensTransform, float baseSize, float magnification)
        {
            _ = lensTransform;
            _ = baseSize;
            _ = magnification;

            if (ScopeHousingMeshSurgeryPlugin.VignetteEnabled.Value)
            {
                EnsureVignetteMeshAndMat();
                RefreshVignetteTexture();
                _vigActive = true;
            }

            if (ScopeHousingMeshSurgeryPlugin.ScopeShadowEnabled.Value)
            {
                EnsureShadowMeshAndMat();
                RefreshShadowTexture();
                _shadowActive = true;
            }

            AttachToCamera();

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[ScopeEffects] Showing: vignette={_vigActive} shadow={_shadowActive} (CommandBuffer)");
        }

        /// <summary>Per-frame update — call from ScopeLifecycle.Tick().</summary>
        public static void UpdateTransform(float baseSize, float magnification)
        {
            _ = baseSize;
            _ = magnification;

            // Refresh textures if config changed
            if (_vigActive)  RefreshVignetteTexture();
            if (_shadowActive) RefreshShadowTexture();

            // Re-attach if camera changed
            var mainCam = ScopeHousingMeshSurgeryPlugin.GetMainCamera();
            if (mainCam != null && mainCam != _attachedCamera)
                AttachToCamera();
        }

        public static void Hide()
        {
            _vigActive = false;
            _shadowActive = false;
            DetachFromCamera();
        }

        public static void Cleanup()
        {
            Hide();
        }

        // ─────────────────────────────────────────────────────────────────────
        // CommandBuffer management
        // ─────────────────────────────────────────────────────────────────────

        private static void AttachToCamera()
        {
            var mainCam = ScopeHousingMeshSurgeryPlugin.GetMainCamera();
            if (mainCam == null) return;

            if (_attachedCamera != null && _attachedCamera != mainCam)
                DetachFromCamera();

            if (_attachedCamera == mainCam) return;

            if (_cmdBuffer == null)
                _cmdBuffer = new CommandBuffer { name = "ScopeEffectsOverlay" };

            mainCam.AddCommandBuffer(CameraEvent.AfterEverything, _cmdBuffer);
            _attachedCamera = mainCam;

            if (!_preCullRegistered)
            {
                Camera.onPreCull += OnPreCullCallback;
                _preCullRegistered = true;
            }

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[ScopeEffects] CommandBuffer attached to '{mainCam.name}' at AfterEverything");
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

        // ─────────────────────────────────────────────────────────────────────
        // onPreCull — rebuild CommandBuffer with final-pose data
        // ─────────────────────────────────────────────────────────────────────

        private static void OnPreCullCallback(Camera cam)
        {
            if (cam != _attachedCamera) return;
            if (_cmdBuffer == null) return;
            if (!_vigActive && !_shadowActive) return;

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
            float screenScale = GetScreenSpaceScale(cam);
            float ndcScale = Mathf.Clamp(3.2f * screenScale, 0.8f, 6f);

            _vigMatrix = Matrix4x4.TRS(
                new Vector3(0f, 0f, 0.6f),
                Quaternion.identity,
                new Vector3(ndcScale, ndcScale, 1f));
        }

        private static void RebuildShadowMatrix(Camera cam)
        {
            if (cam == null) return;

            // Clip-space centered overlay, independent of world/lens transforms.
            // A quad scale of 2 fills the entire viewport in clip-space; keep a
            // floor above that so FOV scaling can never leave uncovered edges.
            float screenScale = GetScreenSpaceScale(cam);
            float ndcScale = Mathf.Max(2.25f, Mathf.Clamp(3.2f * screenScale, 0.8f, 6f));

            _shadowMatrix = Matrix4x4.TRS(
                new Vector3(0f, 0f, 0.7f),
                Quaternion.identity,
                new Vector3(ndcScale, ndcScale, 1f));
        }

        private static void RebuildCommandBuffer(Camera cam)
        {
            _cmdBuffer.Clear();

            _cmdBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            _cmdBuffer.SetViewport(GetDisplayViewport(cam));

            // Pure screen-space draw (clip-space matrices).
            _cmdBuffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);

            // Draw shadow first (behind vignette in render order)
            if (_shadowActive && _shadowMat != null && _shadowMesh != null)
                _cmdBuffer.DrawMesh(_shadowMesh, _shadowMatrix, _shadowMat, 0, -1);

            // Draw vignette on top
            if (_vigActive && _vigMat != null && _vigMesh != null)
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
                _vigMat = new Material(FindAlphaShader())
                {
                    color       = Color.white,
                    renderQueue = 3099
                };
                _vigMat.SetInt("_ZTest", (int)CompareFunction.Always);
                _vigMat.SetInt("_ZWrite", 0);
            }
        }

        /// <summary>
        /// Circular feather with aspect-ratio correction: transparent centre →
        /// smooth darkening toward edge, then keeps darkness outside.
        /// Now screen-filling (like shadow), so X distances are stretched by
        /// the aspect ratio to keep the circle round.
        /// </summary>
        private static void RefreshVignetteTexture()
        {
            float soft = ScopeHousingMeshSurgeryPlugin.VignetteSoftness.Value;
            float opac = ScopeHousingMeshSurgeryPlugin.VignetteOpacity.Value;
            float mult = ScopeHousingMeshSurgeryPlugin.VignetteSizeMult.Value;

            var cam = ScopeHousingMeshSurgeryPlugin.GetMainCamera();
            float aspect = GetDisplayAspect(cam);

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

            // VignetteSizeMult controls where the soft edge begins.
            // mult=1 → feather starts near edge of a circle inscribed in height.
            // mult<1 → feather starts inward (more visible darkening).
            // mult>1 → feather starts outward (less darkening).
            float baseR = mult;
            float innerR = baseR * Mathf.Clamp01(1f - soft);
            float outerR = baseR;

            var pixels = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // Aspect-corrected distance from center (normalized -1..1)
                float nx   = ((float)x / S - 0.5f) * 2f * aspect;
                float ny   = ((float)y / S - 0.5f) * 2f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);

                byte a;
                if (dist <= innerR)
                {
                    a = 0;
                }
                else
                {
                    float t = Mathf.Clamp01((dist - innerR) / Mathf.Max(0.001f, outerR - innerR));

                    // Smoother response at edge so vignette blends into the housing/shadow
                    // instead of reading as a hard dark ring.
                    float feather = Mathf.SmoothStep(0f, 1f, t);
                    feather = Mathf.Pow(feather, 1.35f);

                    a = (byte)(feather * opac * 255f);
                }
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
                _shadowMat = new Material(FindAlphaShader())
                {
                    color       = Color.white,
                    renderQueue = 3050
                };
                _shadowMat.SetInt("_ZTest", (int)CompareFunction.Always);
                _shadowMat.SetInt("_ZWrite", 0);
            }
        }

        /// <summary>
        /// Aspect-ratio corrected shadow texture.  X distances are stretched by
        /// the aspect ratio so the circle appears round on non-square viewports.
        /// </summary>
        private static void RefreshShadowTexture()
        {
            float radius = ScopeHousingMeshSurgeryPlugin.ScopeShadowRadius.Value;
            float soft   = ScopeHousingMeshSurgeryPlugin.ScopeShadowSoftness.Value;
            float opac   = ScopeHousingMeshSurgeryPlugin.ScopeShadowOpacity.Value;

            var cam = ScopeHousingMeshSurgeryPlugin.GetMainCamera();
            Rect viewport = GetDisplayViewport(cam);
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

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[ScopeEffects] Shadow texture rebuilt: {texW}x{texH} aspect={aspect:F2} radius={radius} soft={soft}");
        }

        private static Rect GetDisplayViewport(Camera cam)
        {
            float w = Mathf.Max(1f, Screen.width);
            float h = Mathf.Max(1f, Screen.height);

            // DLSS/FSR may shrink camera pixelRect to internal resolution.
            // Use display-space dimensions so overlays stay centered full-frame.
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

        private static float GetScreenSpaceScale(Camera cam)
        {
            float currentFov = cam != null ? cam.fieldOfView : 35f;
            float referenceFov = Mathf.Max(1f, ScopeHousingMeshSurgeryPlugin.ScopedFov.Value);
            float fovScale = Mathf.Clamp(referenceFov / Mathf.Max(1f, currentFov), 0.5f, 3f);

            // Keep lens effects large enough to match scope housing aperture.
            // Magnification scaling made them collapse too much at higher zoom.
            return fovScale;
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
