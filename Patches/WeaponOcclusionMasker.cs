using System;
using System.Collections.Generic;
using EFT.CameraControl;
using UnityEngine;
using UnityEngine.Rendering;

namespace PiPDisabler
{
    /// <summary>
    /// True stencil/depth aperture masking for weapon meshes while scoped.
    ///
    /// 1) Write rear-lens mesh into stencil (value=1) each frame before opaques.
    /// 2) Override weapon renderer materials to stencil-test NotEqual 1.
    ///
    /// Result: weapon/housing pixels inside the eyepiece aperture are discarded
    /// by GPU stencil test rather than whole-renderer enable/disable heuristics.
    /// </summary>
    internal static class WeaponOcclusionMasker
    {
        private const float StencilRef = 1f;

        private sealed class RendererState
        {
            public Renderer Renderer;
            public Material[] OriginalMaterials;
            public Material[] MaskedMaterials;
        }

        private static readonly List<RendererState> _patchedRenderers = new List<RendererState>(96);

        // Stencil pass state
        private static Renderer _rearLens;
        private static Camera _attachedCamera;
        private static CommandBuffer _cmdBuffer;
        private static bool _preCullRegistered;
        private static Material _lensWriteStencilMat;
        private static Material _stencilClearMat;
        private static Mesh _screenQuad;

        public static void Apply(OpticSight os)
        {
            Restore();

            if (os == null) return;

            var cam = PiPDisablerPlugin.GetMainCamera();
            if (cam == null) return;

            _rearLens = FindClosestLensRenderer(os, cam.transform.position);
            if (_rearLens == null)
            {
                PiPDisablerPlugin.LogInfo("[WeaponOcclusionMasker] No rear lens renderer found");
                return;
            }

            Transform weaponRoot = FindWeaponRoot(os.transform);
            if (weaponRoot == null)
            {
                PiPDisablerPlugin.LogInfo("[WeaponOcclusionMasker] No weapon root found");
                return;
            }

            EnsureStencilMaterials();
            AttachStencilPass(cam);

            var allRenderers = weaponRoot.GetComponentsInChildren<Renderer>(true);
            int patchedCount = 0;

            for (int i = 0; i < allRenderers.Length; i++)
            {
                var r = allRenderers[i];
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                if (ReferenceEquals(r, _rearLens)) continue;

                var original = r.sharedMaterials;
                if (original == null || original.Length == 0) continue;

                var masked = new Material[original.Length];
                bool any = false;

                for (int m = 0; m < original.Length; m++)
                {
                    var src = original[m];
                    if (src == null)
                    {
                        masked[m] = null;
                        continue;
                    }

                    var inst = new Material(src)
                    {
                        name = src.name + "__PiPStencilMasked"
                    };

                    // Keep standard rendering but reject pixels where rear-lens stencil exists.
                    TrySetStencilNotEqual(inst);

                    masked[m] = inst;
                    any = true;
                }

                if (!any)
                {
                    DestroyMaterials(masked);
                    continue;
                }

                _patchedRenderers.Add(new RendererState
                {
                    Renderer = r,
                    OriginalMaterials = original,
                    MaskedMaterials = masked
                });

                r.sharedMaterials = masked;
                patchedCount++;
            }

            PiPDisablerPlugin.LogInfo(
                $"[WeaponOcclusionMasker] Patched {patchedCount} renderer(s) with stencil reject (rearLens='{_rearLens.name}')");
        }

        public static void Restore()
        {
            for (int i = 0; i < _patchedRenderers.Count; i++)
            {
                var e = _patchedRenderers[i];
                if (e.Renderer != null)
                {
                    try { e.Renderer.sharedMaterials = e.OriginalMaterials; }
                    catch { }
                }

                DestroyMaterials(e.MaskedMaterials);
            }
            _patchedRenderers.Clear();

            DetachStencilPass();
            _rearLens = null;
        }

        private static void EnsureStencilMaterials()
        {
            if (_screenQuad == null)
                _screenQuad = BuildFullscreenQuad();

            if (_lensWriteStencilMat != null && _stencilClearMat != null)
                return;

            Shader uiShader = Shader.Find("UI/Default");
            if (uiShader == null)
            {
                PiPDisablerPlugin.LogWarn("[WeaponOcclusionMasker] UI/Default shader not found, stencil mask unavailable");
                return;
            }

            if (_stencilClearMat == null)
            {
                _stencilClearMat = new Material(uiShader) { renderQueue = 1998, name = "PiPStencilClear" };
                _stencilClearMat.SetFloat("_Stencil", 0f);
                _stencilClearMat.SetFloat("_StencilComp", (float)CompareFunction.Always);
                _stencilClearMat.SetFloat("_StencilOp", (float)StencilOp.Replace);
                _stencilClearMat.SetFloat("_StencilWriteMask", 255f);
                _stencilClearMat.SetFloat("_ColorMask", 0f);
                _stencilClearMat.SetInt("_ZTest", (int)CompareFunction.Always);
                _stencilClearMat.SetInt("_ZWrite", 0);
            }

            if (_lensWriteStencilMat == null)
            {
                _lensWriteStencilMat = new Material(uiShader) { renderQueue = 1999, name = "PiPLensStencilWrite" };
                _lensWriteStencilMat.SetFloat("_Stencil", StencilRef);
                _lensWriteStencilMat.SetFloat("_StencilComp", (float)CompareFunction.Always);
                _lensWriteStencilMat.SetFloat("_StencilOp", (float)StencilOp.Replace);
                _lensWriteStencilMat.SetFloat("_StencilWriteMask", 255f);
                _lensWriteStencilMat.SetFloat("_ColorMask", 0f);
                _lensWriteStencilMat.SetInt("_ZTest", (int)CompareFunction.Always);
                _lensWriteStencilMat.SetInt("_ZWrite", 0);
                _lensWriteStencilMat.SetInt("_Cull", (int)CullMode.Off);
            }
        }

        private static void AttachStencilPass(Camera cam)
        {
            if (cam == null || _lensWriteStencilMat == null || _stencilClearMat == null || _screenQuad == null)
                return;

            if (_attachedCamera != null && _attachedCamera != cam)
                DetachStencilPass();

            if (_cmdBuffer == null)
                _cmdBuffer = new CommandBuffer { name = "PiP Weapon Lens Stencil" };

            if (_attachedCamera == null)
            {
                _attachedCamera = cam;
                _attachedCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, _cmdBuffer);
                _attachedCamera.AddCommandBuffer(CameraEvent.BeforeGBuffer, _cmdBuffer);
            }

            if (!_preCullRegistered)
            {
                Camera.onPreCull += OnPreCull;
                _preCullRegistered = true;
            }
        }

        private static void DetachStencilPass()
        {
            if (_preCullRegistered)
            {
                Camera.onPreCull -= OnPreCull;
                _preCullRegistered = false;
            }

            if (_attachedCamera != null && _cmdBuffer != null)
            {
                try { _attachedCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, _cmdBuffer); } catch { }
                try { _attachedCamera.RemoveCommandBuffer(CameraEvent.BeforeGBuffer, _cmdBuffer); } catch { }
            }

            if (_cmdBuffer != null)
                _cmdBuffer.Clear();

            _attachedCamera = null;
        }

        private static void OnPreCull(Camera cam)
        {
            if (cam == null || cam != _attachedCamera || _cmdBuffer == null)
                return;

            _cmdBuffer.Clear();

            if (_rearLens == null || !_rearLens.enabled || !_rearLens.gameObject.activeInHierarchy)
                return;

            // Clear stencil to 0 across full screen, then write rear-lens aperture as 1.
            var fullScreen = Matrix4x4.TRS(new Vector3(0f, 0f, 0.5f), Quaternion.identity, new Vector3(2f, 2f, 1f));
            _cmdBuffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            _cmdBuffer.DrawMesh(_screenQuad, fullScreen, _stencilClearMat, 0, -1);
            _cmdBuffer.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);

            _cmdBuffer.DrawRenderer(_rearLens, _lensWriteStencilMat);
        }

        private static void TrySetStencilNotEqual(Material m)
        {
            if (m == null) return;

            m.SetFloat("_Stencil", StencilRef);
            m.SetFloat("_StencilComp", (float)CompareFunction.NotEqual);
            m.SetFloat("_StencilOp", (float)StencilOp.Keep);
            m.SetFloat("_StencilReadMask", 255f);
            m.SetFloat("_StencilWriteMask", 255f);
        }

        private static void DestroyMaterials(Material[] mats)
        {
            if (mats == null) return;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null)
                {
                    try { UnityEngine.Object.Destroy(mats[i]); }
                    catch { }
                }
            }
        }

        private static Mesh BuildFullscreenQuad()
        {
            var mesh = new Mesh { name = "PiP_StencilFullScreenQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3( 1f, -1f, 0f),
                new Vector3(-1f,  1f, 0f),
                new Vector3( 1f,  1f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Renderer FindClosestLensRenderer(OpticSight os, Vector3 cameraPos)
        {
            if (os == null) return null;

            var renderers = os.transform.GetComponentsInChildren<Renderer>(true);
            Renderer best = null;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.gameObject.activeInHierarchy) continue;

                string n = (r.gameObject.name ?? string.Empty).ToLowerInvariant();
                if (!(n.Contains("backlens") || n.Contains("back_lens") || n.Contains("linza") || n.Contains("lens") || n.Contains("glass")))
                    continue;

                float d = (r.bounds.center - cameraPos).sqrMagnitude;
                if (d < bestDistSq)
                {
                    best = r;
                    bestDistSq = d;
                }
            }

            return best ?? os.LensRenderer;
        }

        private static Transform FindWeaponRoot(Transform from)
        {
            for (Transform cur = from; cur != null; cur = cur.parent)
            {
                string n = cur.name ?? string.Empty;
                if (string.Equals(n, "weapon", StringComparison.OrdinalIgnoreCase))
                    return cur;
            }
            return null;
        }
    }
}
