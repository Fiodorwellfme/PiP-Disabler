using System;
using System.Collections.Generic;
using EFT.CameraControl;
using UnityEngine;
using UnityEngine.Rendering;

namespace PiPDisabler
{
    /// <summary>
    /// Experimental "pixel hole" (2.8.5): the scope body is hidden inside the lens circle on screen,
    /// per pixel, instead of cutting its meshes. Nothing is cut, so zoom (EFT stretches the weapon),
    /// restarts and recoil need no re-cut and the hole edge is the lens circle itself.
    ///
    /// EFT renders the main camera deferred. A command buffer at BeforeGBuffer draws the weapon parts
    /// that cover the lens (with their own materials, deferred pass) and then a disk over the lens
    /// circle that only writes depth = far plane (no colour). The normal G-buffer pass runs after it:
    /// inside the circle the world now passes the depth test and overwrites the scope body, outside
    /// it the body stays in front. The world's depth stays its own, so lighting, fog and smoke are
    /// unchanged. Those parts are set to ShadowsOnly meanwhile (not drawn twice, still cast shadows).
    /// Off by default: depends on render details that can only be checked in game (see README).
    /// </summary>
    internal static class PixelHole
    {
        private const int RimSegments = 128;
        // Disk depth in normalised device coordinates (OpenGL style): exactly the far plane, the same depth
        // a pixel has where nothing was drawn. 2.8.6 used 0.999995 (~5-7 km at far 10000), which is in
        // front of the sky dome (TOD puts it at ~0.95 × far): the sky failed its depth test inside the
        // lens and the scope's inside showed when aiming at the sky (2.8.8). The disk is drawn with
        // identity matrices, so the value reaches the GPU exactly (no round trip through the camera
        // matrices that could push it past the far plane and clip it).
        private const float DiskNdcZ = 1f;

        private static OpticSight _os;
        private static Transform _scopeRoot, _weaponRoot;
        private static Vector3 _planeLocal, _normalLocal;
        private static float _holeR;
        private static Camera _cam;
        private static CommandBuffer _cb;
        private static Material _depthMat;
        // Black G-buffer surface for the circle (2.8.9): albedo, specular and emission 0, drawn after the
        // depth reset with the far depth. Without it the G-buffer inside the circle kept the scope body:
        // where no world geometry covers it (sky) the body was lit and the sky, which EFT blends on top,
        // let it show through faintly. -1 = no usable shader (only the depth reset is drawn).
        private static Material _blackMat;
        private static int _blackPass = -1;
        private static string _blackShader = "none";
        private static Mesh _disk;
        private static readonly Vector3[] _diskVerts = new Vector3[RimSegments + 1];
        private static readonly List<Renderer> _candidates = new List<Renderer>(64);
        private static readonly HashSet<Renderer> _lodHidden = new HashSet<Renderer>();
        private static readonly Dictionary<Renderer, ShadowCastingMode> _moved = new Dictionary<Renderer, ShadowCastingMode>();
        private static readonly Dictionary<Material, int> _deferredPass = new Dictionary<Material, int>();
        private static readonly Vector3[] _corners = new Vector3[8];
        private static readonly List<Material> _mats = new List<Material>(4);
        private static bool _failedThisSession;
        private static bool _loggedFrame;

        internal static bool Enabled => Settings.PixelHoleMode != null && Settings.PixelHoleMode.Value && !_failedThisSession;
        internal static bool Active => _os != null;

        /// <summary>Start hiding the body inside the lens for this scope. False → use the mesh cut.</summary>
        internal static bool TryBegin(OpticSight os)
        {
            End();
            if (!Enabled || os == null) return false;
            try
            {
                var cam = Helpers.GetMainCamera();
                if (cam == null) return false;
                if (cam.actualRenderingPath != RenderingPath.DeferredShading)
                {
                    Fail($"main camera renders {cam.actualRenderingPath}, not deferred");
                    return false;
                }
                var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
                if (!scopeRoot) return false;
                var activeMode = MeshSurgeryManager.ResolveActiveModeFor(os, scopeRoot);
                if (!activeMode) activeMode = os.transform;
                if (!ScopeHierarchy.TryGetPlane(os, scopeRoot, activeMode, out var planePoint, out var planeNormal, out _))
                    return false;
                planePoint += planeNormal * PerScopeMeshSurgerySettings.GetPlane1OffsetMeters();
                float lensR = LensTransparency.GetEyepieceLensRadius(scopeRoot);
                if (lensR <= 0.003f || lensR >= 0.05f)
                {
                    PiPDisablerPlugin.LogSource.LogInfo($"[PixelHole] Eyepiece lens radius {lensR * 1000f:F1}mm not usable — mesh cut used");
                    return false;
                }
                if (!EnsureResources()) return false;

                _scopeRoot = scopeRoot;
                _weaponRoot = MeshSurgeryManager.FindWeaponTransform(scopeRoot);
                _planeLocal = scopeRoot.InverseTransformPoint(planePoint);
                _normalLocal = scopeRoot.InverseTransformDirection(planeNormal);
                // Same edge as the automatic cut at the lens plane (0.97 × lens radius × width).
                _holeR = lensR * 0.97f * Mathf.Clamp(PerScopeMeshSurgerySettings.GetCutWidthMultiplierRaw(), 0.5f, 1.5f);

                _candidates.Clear();
                foreach (var mf in ScopeHierarchy.FindTargetMeshFilters(scopeRoot, activeMode))
                {
                    var r = mf != null ? mf.GetComponent<Renderer>() : null;
                    if (r != null && !(r is SkinnedMeshRenderer)) _candidates.Add(r);
                }
                _lodHidden.Clear();
                if (_weaponRoot != null)
                    foreach (var g in _weaponRoot.GetComponentsInChildren<LODGroup>(true))
                    {
                        var lods = g.GetLODs();
                        for (int i = 1; i < lods.Length; i++)
                            foreach (var lr in lods[i].renderers) if (lr != null) _lodHidden.Add(lr);
                    }

                _cb = new CommandBuffer { name = "PiPDisabler PixelHole" };
                cam.AddCommandBuffer(CameraEvent.BeforeGBuffer, _cb);
                _cam = cam;
                _os = os;
                _loggedFrame = false;
                MeshSurgeryManager.DisableScopeExtras(scopeRoot);
                MeshSurgeryManager.EnsureRenderHook();
                PiPDisablerPlugin.LogSource.LogInfo(
                    $"[PixelHole] On for '{os.name}': lens r={lensR * 1000f:F1}mm, hole r={_holeR * 1000f:F1}mm, {_candidates.Count} weapon parts checked per frame, camera far={cam.farClipPlane:F0}m (projection far {ProjectionFar(cam)}){OtherBuffers(cam)}");
                return true;
            }
            catch (Exception ex)
            {
                Fail("start failed: " + ex.Message);
                End();
                return false;
            }
        }

        // Far distance actually in the projection matrix (EFT may set its own matrix).
        private static string ProjectionFar(Camera c)
        {
            var m = c.projectionMatrix;
            float d = m.m22 + 1f;
            return Mathf.Abs(d) < 1e-7f ? "infinite" : (m.m23 / d).ToString("F0") + "m";
        }

        // Other command buffers around the G-buffer pass (for diagnosing raid-only differences).
        private static string OtherBuffers(Camera c)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ev in new[] { CameraEvent.BeforeGBuffer, CameraEvent.AfterGBuffer })
                foreach (var b in c.GetCommandBuffers(ev))
                    if (b != _cb) sb.Append(sb.Length == 0 ? "; other buffers: " : ", ").Append(ev).Append('=').Append(b.name);
            return sb.ToString();
        }

        internal static void End()
        {
            if (_cb != null)
            {
                try { if (_cam != null) _cam.RemoveCommandBuffer(CameraEvent.BeforeGBuffer, _cb); } catch { }
                try { _cb.Release(); } catch { }
                _cb = null;
            }
            foreach (var kv in _moved)
                if (kv.Key != null)
                    try { kv.Key.shadowCastingMode = kv.Value; } catch { }
            _moved.Clear();
            if (_os != null)
            {
                if (_weaponRoot != null) MeshSurgeryManager.RestoreScopeExtras(_weaponRoot);
                PiPDisablerPlugin.DebugLogInfo("[PixelHole] Off");
            }
            _os = null;
            _scopeRoot = null;
            _weaponRoot = null;
            _cam = null;
            _candidates.Clear();
            _lodHidden.Clear();
        }

        /// <summary>Main camera, before culling (weapon as on screen): choose parts, record the buffer.</summary>
        internal static void OnPreCullMain(Camera c)
        {
            if (_os == null || _cb == null) return;
            if (c != _cam || _scopeRoot == null || _os == null)
            {
                End();
                return;
            }
            try { Record(c); }
            catch (Exception ex)
            {
                Fail("frame failed: " + ex.Message);
                End();
            }
        }

        private static void Record(Camera c)
        {
            _cb.Clear();
            Vector3 p = _scopeRoot.TransformPoint(_planeLocal);
            Vector3 n = _scopeRoot.TransformDirection(_normalLocal).normalized;
            Vector3 camPos = c.transform.position;
            Vector3 fwd = c.transform.forward;
            float lensDepth = Vector3.Dot(p - camPos, fwd);
            if (lensDepth <= c.nearClipPlane)
            {
                RestoreMoved();
                return; // lens not in front of the camera (aim animation): nothing to hide
            }

            // Lens circle (on the lens plane) on screen. The disk is given straight in device coordinates
            // with depth fixed at the far plane, drawn with identity matrices: it no longer
            // depends on the camera's far distance (2.8.6: at far=10000m in raid the old disk placed at
            // 0.9995 × far did not land on screen and the hole stayed black — the scope's inside showed).
            Vector3 u = Vector3.Cross(n, Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 v = Vector3.Cross(n, u);
            Matrix4x4 vp = c.projectionMatrix * c.worldToCameraMatrix;
            Vector2 sc = c.WorldToScreenPoint(p);
            JitterProbe.Hole(sc);
            float rPx = 0f;
            if (!ToNdc(vp, p, out _diskVerts[0])) { RestoreMoved(); return; }
            for (int k = 0; k < RimSegments; k++)
            {
                float a = k * (2f * Mathf.PI / RimSegments);
                Vector3 q = p + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * _holeR;
                if (Vector3.Dot(q - camPos, fwd) <= c.nearClipPlane || !ToNdc(vp, q, out _diskVerts[k + 1]))
                {
                    RestoreMoved();
                    return;
                }
                Vector2 sq = c.WorldToScreenPoint(q);
                rPx = Mathf.Max(rPx, (sq - sc).magnitude);
            }
            _disk.vertices = _diskVerts;
            _disk.RecalculateBounds();

            // Parts whose screen box touches the lens circle are drawn by us (then hidden inside it).
            float reach = rPx * 1.05f + 4f;
            int drawn = 0, forwardOnly = 0, moved = 0;
            foreach (var r in _candidates)
            {
                bool use = r != null && r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy &&
                           !_lodHidden.Contains(r) && !LodLock.IsNotDrawn(r) && Touches(c, r.bounds, sc, reach);
                if (!use)
                {
                    if (r != null && _moved.TryGetValue(r, out var old)) { r.shadowCastingMode = old; _moved.Remove(r); }
                    continue;
                }
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                if (!_moved.ContainsKey(r))
                {
                    _moved[r] = r.shadowCastingMode;
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
                moved++;
                var mats = _mats;
                r.GetSharedMaterials(mats);
                int subs = Mathf.Min(mats.Count, mesh.subMeshCount);
                for (int i = 0; i < subs; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    int pass = DeferredPass(m);
                    if (pass < 0) { forwardOnly++; continue; } // transparent/forward-only part: hidden while it covers the lens
                    _cb.DrawRenderer(r, m, i, pass);
                    drawn++;
                }
            }
            // Disk vertices are device coordinates: draw with identity matrices (Unity still applies the
            // platform's Y flip / reversed depth on top), then give the G-buffer pass the camera's back.
            _cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            _cb.DrawMesh(_disk, Matrix4x4.identity, _depthMat, 0, 0);
            if (_blackPass >= 0) _cb.DrawMesh(_disk, Matrix4x4.identity, _blackMat, 1, _blackPass);
            _cb.SetViewProjectionMatrices(c.worldToCameraMatrix, c.projectionMatrix);

            if (!_loggedFrame)
            {
                _loggedFrame = true;
                PiPDisablerPlugin.LogSource.LogInfo(
                    $"[PixelHole] First frame: lens {rPx:F0}px on screen, {moved} parts cover it ({drawn} drawn by the mod, {forwardOnly} forward-only parts hidden while covering it), black surface shader: {_blackShader}");
            }
        }

        private static bool ToNdc(Matrix4x4 vp, Vector3 w, out Vector3 ndc)
        {
            Vector4 h = vp * new Vector4(w.x, w.y, w.z, 1f);
            if (h.w <= 1e-5f) { ndc = default; return false; }
            ndc = new Vector3(h.x / h.w, h.y / h.w, DiskNdcZ);
            return true;
        }

        // Screen box of the world bounds touches the circle (centre sc, radius reach px)?
        private static bool Touches(Camera c, Bounds b, Vector2 sc, float reach)
        {
            Vector3 mn = b.min, mx = b.max;
            _corners[0] = new Vector3(mn.x, mn.y, mn.z); _corners[1] = new Vector3(mx.x, mn.y, mn.z);
            _corners[2] = new Vector3(mn.x, mx.y, mn.z); _corners[3] = new Vector3(mx.x, mx.y, mn.z);
            _corners[4] = new Vector3(mn.x, mn.y, mx.z); _corners[5] = new Vector3(mx.x, mn.y, mx.z);
            _corners[6] = new Vector3(mn.x, mx.y, mx.z); _corners[7] = new Vector3(mx.x, mx.y, mx.z);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 s = c.WorldToScreenPoint(_corners[i]);
                if (s.z <= c.nearClipPlane) return true; // reaches behind the camera: be safe
                x0 = Mathf.Min(x0, s.x); y0 = Mathf.Min(y0, s.y);
                x1 = Mathf.Max(x1, s.x); y1 = Mathf.Max(y1, s.y);
            }
            float dx = Mathf.Max(0f, Mathf.Max(x0 - sc.x, sc.x - x1));
            float dy = Mathf.Max(0f, Mathf.Max(y0 - sc.y, sc.y - y1));
            return dx * dx + dy * dy < reach * reach;
        }

        private static int DeferredPass(Material m)
        {
            if (_deferredPass.TryGetValue(m, out int p)) return p;
            p = -1;
            try { p = FindDeferredPass(m); } catch { p = -1; }
            _deferredPass[m] = p;
            return p;
        }

        private static int FindDeferredPass(Material m)
        {
            var shader = m.shader;
            if (shader == null) return -1;
            var tag = new ShaderTagId("LightMode");
            for (int i = 0; i < m.passCount; i++)
                if (string.Equals(shader.FindPassTagValue(i, tag).name, "Deferred", StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static void RestoreMoved()
        {
            foreach (var kv in _moved)
                if (kv.Key != null) kv.Key.shadowCastingMode = kv.Value;
            _moved.Clear();
        }

        private static bool EnsureResources()
        {
            if (_depthMat == null)
            {
                var sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) { Fail("built-in shader Hidden/Internal-Colored not found"); return false; }
                _depthMat = new Material(sh) { name = "PiPDisabler PixelHole depth", hideFlags = HideFlags.HideAndDontSave };
                _depthMat.SetInt("_SrcBlend", (int)BlendMode.Zero); // colour (all G-buffer targets) unchanged
                _depthMat.SetInt("_DstBlend", (int)BlendMode.One);
                _depthMat.SetInt("_Cull", (int)CullMode.Off);
                _depthMat.SetInt("_ZWrite", 1);
                _depthMat.SetInt("_ZTest", (int)CompareFunction.Always);
                CreateBlackMaterial();
            }
            if (_disk == null)
            {
                _disk = new Mesh { name = "PiPDisabler PixelHole disk", hideFlags = HideFlags.HideAndDontSave };
                _disk.MarkDynamic();
                _disk.vertices = _diskVerts;
                var tris = new int[RimSegments * 3];
                for (int k = 0; k < RimSegments; k++)
                {
                    tris[k * 3] = 0;
                    tris[k * 3 + 1] = 1 + k;
                    tris[k * 3 + 2] = 1 + (k + 1) % RimSegments;
                }
                // Submesh 0: depth reset (cull off). Submesh 1: both windings, for the black surface whose
                // shader culls back faces (the on-screen winding depends on the lens plane / Y flip).
                var both = new int[RimSegments * 6];
                for (int k = 0; k < RimSegments; k++)
                {
                    both[k * 6] = 0; both[k * 6 + 1] = 1 + k; both[k * 6 + 2] = 1 + (k + 1) % RimSegments;
                    both[k * 6 + 3] = 0; both[k * 6 + 4] = 1 + (k + 1) % RimSegments; both[k * 6 + 5] = 1 + k;
                }
                _disk.subMeshCount = 2;
                _disk.SetTriangles(tris, 0);
                _disk.SetTriangles(both, 1);
                // Valid normals (a zero normal would give NaN in lighting); the surface is black anyway.
                var normals = new Vector3[RimSegments + 1];
                for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
                _disk.normals = normals;
            }
            return true;
        }

        // Built-in Standard shader's deferred pass writes all G-buffer targets (with Unity's own LDR/HDR
        // light-buffer encoding). Specular setup with black specular = no reflections either; the metallic
        // Standard (dielectric 4 % specular) is the fallback. Missing shader → depth reset only.
        private static void CreateBlackMaterial()
        {
            foreach (var name in new[] { "Standard (Specular setup)", "Standard" })
            {
                var sh = Shader.Find(name);
                if (sh == null) continue;
                var m = new Material(sh) { name = "PiPDisabler PixelHole black", hideFlags = HideFlags.HideAndDontSave };
                m.SetColor("_Color", Color.black);
                m.SetColor("_SpecColor", Color.black);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Glossiness", 0f);
                m.SetColor("_EmissionColor", Color.black);
                m.DisableKeyword("_EMISSION");
                int pass = FindDeferredPass(m);
                if (pass < 0) { UnityEngine.Object.Destroy(m); continue; }
                _blackMat = m;
                _blackPass = pass;
                _blackShader = name;
                return;
            }
            PiPDisablerPlugin.LogSource.LogWarning("[PixelHole] No Standard shader with a deferred pass — the sky may show the scope's inside faintly through the lens");
        }

        private static void Fail(string why)
        {
            _failedThisSession = true;
            PiPDisablerPlugin.LogSource.LogWarning($"[PixelHole] Turned off for this game session ({why}) — mesh cut used instead");
        }
    }
}
