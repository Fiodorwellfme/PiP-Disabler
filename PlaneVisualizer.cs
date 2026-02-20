using System.Collections.Generic;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Visualizes the mesh cutting volume in two modes:
    /// 1. ShowCutPlane: Near plane (green) + far plane (red) as circles/quads.
    /// 2. ShowCutVolume: Full 3D semi-transparent tube showing the frustum walls
    ///    with near→mid→far radius profile.
    /// </summary>
    internal static class PlaneVisualizer
    {
        // Endpoint circles (ShowCutPlane)
        private static GameObject _nearGO;
        private static GameObject _farGO;
        private static Material _nearMat;
        private static Material _farMat;
        private static Mesh _quadMesh;
        private static Mesh _circleMesh;

        // 3D tube volume (ShowCutVolume)
        private static GameObject _tubeGO;
        private static Material _tubeMat;
        private static Mesh _tubeMesh;
        private static int _lastTubeHash; // detect config changes to regenerate

        // Preserve zone ring (ShowCutVolume)
        private static GameObject _preserveRingGO;
        private static Material _preserveRingMat;

        public static void Show(Vector3 planePoint, Vector3 planeNormal)
        {
            bool showPlane = ScopeHousingMeshSurgeryPlugin.ShowCutPlane.Value;
            bool showVolume = ScopeHousingMeshSurgeryPlugin.ShowCutVolume.Value;

            if (!showPlane && !showVolume)
            {
                Hide();
                return;
            }

            bool isCylinder = ScopeHousingMeshSurgeryPlugin.CutMode.Value == "Cylinder";
            float startOffset = ScopeHousingMeshSurgeryPlugin.CutStartOffset.Value;
            float cutLength = ScopeHousingMeshSurgeryPlugin.CutLength.Value;

            Vector3 nearPos = planePoint - planeNormal * startOffset;
            Vector3 farPos = nearPos + planeNormal * cutLength;
            Quaternion rot = Quaternion.LookRotation(planeNormal);

            // --- Endpoint circles (ShowCutPlane) ---
            if (showPlane)
            {
                EnsureCirclesCreated();

                _nearGO.SetActive(true);
                _nearGO.transform.position = nearPos;
                _nearGO.transform.rotation = rot;

                _farGO.SetActive(true);
                _farGO.transform.position = farPos;
                _farGO.transform.rotation = rot;

                if (isCylinder)
                {
                    float nearR = ScopeHousingMeshSurgeryPlugin.CylinderRadius.Value;
                    float farR = ScopeHousingMeshSurgeryPlugin.FarCylinderRadius.Value;
                    if (farR <= 0f) farR = nearR;

                    _nearGO.transform.localScale = Vector3.one * nearR * 2f;
                    _farGO.transform.localScale = Vector3.one * farR * 2f;
                    _nearGO.GetComponent<MeshFilter>().sharedMesh = GetCircleMesh();
                    _farGO.GetComponent<MeshFilter>().sharedMesh = GetCircleMesh();
                }
                else
                {
                    float s = 0.015f;
                    _nearGO.transform.localScale = Vector3.one * s;
                    _farGO.transform.localScale = Vector3.one * s;
                    _nearGO.GetComponent<MeshFilter>().sharedMesh = GetQuadMesh();
                    _farGO.GetComponent<MeshFilter>().sharedMesh = GetQuadMesh();
                }
            }
            else
            {
                if (_nearGO != null) _nearGO.SetActive(false);
                if (_farGO != null) _farGO.SetActive(false);
            }

            // --- 3D tube volume (ShowCutVolume) ---
            if (showVolume && isCylinder)
            {
                float nearR = ScopeHousingMeshSurgeryPlugin.CylinderRadius.Value;
                float farR = ScopeHousingMeshSurgeryPlugin.FarCylinderRadius.Value;
                float midR = ScopeHousingMeshSurgeryPlugin.MidCylinderRadius.Value;
                float midPos = ScopeHousingMeshSurgeryPlugin.MidCylinderPosition.Value;
                float opacity = ScopeHousingMeshSurgeryPlugin.CutVolumeOpacity.Value;
                if (farR <= 0f) farR = nearR;

                int hash = ComputeTubeHash(nearR, midR, midPos, farR, cutLength);
                EnsureTubeCreated(nearR, midR, midPos, farR, cutLength, opacity, hash);

                // Update opacity in real-time without regenerating mesh
                if (_tubeMat != null)
                {
                    var c = _tubeMat.color;
                    if (Mathf.Abs(c.a - opacity) > 0.01f)
                    {
                        c.a = opacity;
                        _tubeMat.color = c;
                    }
                }

                _tubeGO.SetActive(true);
                // Position at the near end, oriented along the bore axis
                _tubeGO.transform.position = nearPos;
                _tubeGO.transform.rotation = rot;
                _tubeGO.transform.localScale = Vector3.one;

                // Preserve zone ring — yellow circle at the depth where cutting actually begins
                float preserve = ScopeHousingMeshSurgeryPlugin.NearPreserveDepth.Value;
                if (preserve > 0f)
                {
                    EnsurePreserveRingCreated();
                    // Compute the radius at the preserve boundary using the same profile
                    float preserveT = cutLength > 1e-5f ? Mathf.Clamp01(preserve / cutLength) : 0f;
                    float preserveR = MeshPlaneCutter.RadiusAtT(preserveT, nearR, midR, midPos, farR);
                    Vector3 preservePos = nearPos + planeNormal * preserve;
                    _preserveRingGO.SetActive(true);
                    _preserveRingGO.transform.position = preservePos;
                    _preserveRingGO.transform.rotation = rot;
                    _preserveRingGO.transform.localScale = Vector3.one * preserveR * 2f;
                    _preserveRingGO.GetComponent<MeshFilter>().sharedMesh = GetCircleMesh();
                }
                else
                {
                    if (_preserveRingGO != null) _preserveRingGO.SetActive(false);
                }
            }
            else
            {
                if (_tubeGO != null) _tubeGO.SetActive(false);
                if (_preserveRingGO != null) _preserveRingGO.SetActive(false);
            }
        }

        public static void Hide()
        {
            if (_nearGO != null) _nearGO.SetActive(false);
            if (_farGO != null) _farGO.SetActive(false);
            if (_tubeGO != null) _tubeGO.SetActive(false);
            if (_preserveRingGO != null) _preserveRingGO.SetActive(false);
        }

        public static void Destroy()
        {
            SafeDestroy(ref _nearGO); SafeDestroy(ref _farGO);
            SafeDestroy(ref _nearMat); SafeDestroy(ref _farMat);
            SafeDestroy(ref _quadMesh); SafeDestroy(ref _circleMesh);
            SafeDestroy(ref _tubeGO); SafeDestroy(ref _tubeMat); SafeDestroy(ref _tubeMesh);
            SafeDestroy(ref _preserveRingGO); SafeDestroy(ref _preserveRingMat);
            _lastTubeHash = 0;
        }

        private static void SafeDestroy<T>(ref T obj) where T : Object
        { if (obj != null) { Object.Destroy(obj); obj = null; } }

        // ============================
        //  Endpoint circles
        // ============================
        private static void EnsureCirclesCreated()
        {
            if (_nearGO != null && _farGO != null) return;
            _nearMat = MakeMat(new Color(0, 1, 0, 0.35f));
            _farMat = MakeMat(new Color(1, 0, 0, 0.35f));
            _nearGO = MakeGO("CutPlaneVis_Near", _nearMat);
            _farGO = MakeGO("CutPlaneVis_Far", _farMat);
            ScopeHousingMeshSurgeryPlugin.LogInfo("[PlaneVis] Created near (green) + far (red)");
        }

        private static void EnsurePreserveRingCreated()
        {
            if (_preserveRingGO != null) return;
            _preserveRingMat = MakeMat(new Color(1, 1, 0, 0.5f)); // yellow
            _preserveRingGO = MakeGO("CutPlaneVis_Preserve", _preserveRingMat);
            ScopeHousingMeshSurgeryPlugin.LogInfo("[PlaneVis] Created preserve zone ring (yellow)");
        }

        // ============================
        //  3D Tube Volume
        // ============================

        private static int ComputeTubeHash(float nearR, float midR, float midPos, float farR, float len)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + nearR.GetHashCode();
                h = h * 31 + midR.GetHashCode();
                h = h * 31 + midPos.GetHashCode();
                h = h * 31 + farR.GetHashCode();
                h = h * 31 + len.GetHashCode();
                return h;
            }
        }

        private static void EnsureTubeCreated(float nearR, float midR, float midPos, float farR,
            float cutLen, float opacity, int hash)
        {
            if (_tubeGO != null && _lastTubeHash == hash) return;

            // Regenerate mesh when config changes
            if (_tubeMesh != null) Object.Destroy(_tubeMesh);
            _tubeMesh = GenerateTubeMesh(nearR, midR, midPos, farR, cutLen);

            if (_tubeGO == null)
            {
                _tubeMat = MakeMat(new Color(0.2f, 0.6f, 1f, opacity));
                _tubeGO = MakeGO("CutVolumeVis", _tubeMat);
                ScopeHousingMeshSurgeryPlugin.LogInfo("[PlaneVis] Created 3D cut volume visualizer");
            }

            _tubeGO.GetComponent<MeshFilter>().sharedMesh = _tubeMesh;
            _lastTubeHash = hash;
        }

        /// <summary>
        /// Generate a tube mesh with near→mid→far radius profile.
        /// The tube extends along local Z from 0 to cutLen.
        /// Double-sided (visible from both inside and outside the bore).
        /// End caps included so the volume appears as a solid shape.
        /// </summary>
        private static Mesh GenerateTubeMesh(float nearR, float midR, float midPos, float farR, float cutLen)
        {
            const int segments = 32;   // circumferential resolution
            const int rings = 20;      // longitudinal resolution

            var verts = new List<Vector3>((segments + 1) * (rings + 1) + (segments + 2) * 2);
            var norms = new List<Vector3>(verts.Capacity);
            var uvs = new List<Vector2>(verts.Capacity);
            var tris = new List<int>(segments * rings * 12 + segments * 12);

            // ── Tube wall rings ─────────────────────────────────────────
            for (int r = 0; r <= rings; r++)
            {
                float t = (float)r / rings;
                float z = t * cutLen;
                float radius = GetRadiusAtT(t, nearR, midR, midPos, farR);

                for (int s = 0; s <= segments; s++)
                {
                    float angle = 2f * Mathf.PI * s / segments;
                    float x = Mathf.Cos(angle) * radius;
                    float y = Mathf.Sin(angle) * radius;

                    verts.Add(new Vector3(x, y, z));
                    norms.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f));
                    uvs.Add(new Vector2((float)s / segments, t));
                }
            }

            // ── Tube wall triangles (double-sided) ──────────────────────
            int vertsPerRing = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int i0 = r * vertsPerRing + s;
                    int i1 = i0 + 1;
                    int i2 = i0 + vertsPerRing;
                    int i3 = i2 + 1;

                    // Outside face
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);

                    // Inside face (visible from within the bore)
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    tris.Add(i1); tris.Add(i3); tris.Add(i2);
                }
            }

            // ── Near cap (z = 0) ────────────────────────────────────────
            AddCapDisc(verts, norms, uvs, tris, segments,
                GetRadiusAtT(0f, nearR, midR, midPos, farR), 0f, -Vector3.forward);

            // ── Far cap (z = cutLen) ────────────────────────────────────
            AddCapDisc(verts, norms, uvs, tris, segments,
                GetRadiusAtT(1f, nearR, midR, midPos, farR), cutLen, Vector3.forward);

            var mesh = new Mesh { name = "CutVolumeTube" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[PlaneVis] Generated tube: verts={verts.Count} tris={tris.Count / 3} " +
                $"nearR={nearR:F4} midR={midR:F4}@{midPos:F2} farR={farR:F4} len={cutLen:F3}");

            return mesh;
        }

        private static void AddCapDisc(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs,
            List<int> tris, int segments, float radius, float z, Vector3 normal)
        {
            int centerIdx = verts.Count;
            verts.Add(new Vector3(0, 0, z));
            norms.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int s = 0; s <= segments; s++)
            {
                float angle = 2f * Mathf.PI * s / segments;
                verts.Add(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, z));
                norms.Add(normal);
                uvs.Add(new Vector2(Mathf.Cos(angle) * 0.5f + 0.5f, Mathf.Sin(angle) * 0.5f + 0.5f));
            }

            bool facingForward = Vector3.Dot(normal, Vector3.forward) > 0;
            for (int s = 0; s < segments; s++)
            {
                int a = centerIdx + 1 + s;
                int b = centerIdx + 1 + (s + 1) % (segments + 1);
                // Wind correctly based on normal direction, then add both sides
                if (facingForward)
                { tris.Add(centerIdx); tris.Add(b); tris.Add(a); }
                else
                { tris.Add(centerIdx); tris.Add(a); tris.Add(b); }
                // Reverse for inside visibility
                if (facingForward)
                { tris.Add(centerIdx); tris.Add(a); tris.Add(b); }
                else
                { tris.Add(centerIdx); tris.Add(b); tris.Add(a); }
            }
        }

        /// <summary>
        /// Compute radius at normalized position t (0=near, 1=far).
        /// Delegates to MeshPlaneCutter.RadiusAtT so visualizer and cutter always agree.
        /// </summary>
        private static float GetRadiusAtT(float t, float nearR, float midR, float midPos, float farR)
        {
            return MeshPlaneCutter.RadiusAtT(t, nearR, midR, midPos, farR);
        }

        // ============================
        //  Shared helpers
        // ============================

        private static Material MakeMat(Color c)
        {
            var sh = Shader.Find("Legacy Shaders/Transparent/Diffuse")
                  ?? Shader.Find("Transparent/Diffuse") ?? Shader.Find("UI/Default");
            var m = new Material(sh ?? Shader.Find("Standard"));
            m.color = c; m.renderQueue = 3500; return m;
        }

        private static GameObject MakeGO(string name, Material mat)
        {
            var go = new GameObject(name);
            Object.DontDestroyOnLoad(go);
            go.AddComponent<MeshFilter>().sharedMesh = GetQuadMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.layer = 0;
            return go;
        }

        private static Mesh GetQuadMesh()
        {
            if (_quadMesh != null) return _quadMesh;
            _quadMesh = new Mesh { name = "VisQuad" };
            _quadMesh.vertices = new[] {
                new Vector3(-0.5f,0,-0.5f), new Vector3(0.5f,0,-0.5f),
                new Vector3(0.5f,0,0.5f), new Vector3(-0.5f,0,0.5f) };
            _quadMesh.triangles = new[] { 0,2,1, 0,3,2, 0,1,2, 0,2,3 };
            _quadMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            _quadMesh.RecalculateBounds();
            return _quadMesh;
        }

        private static Mesh GetCircleMesh()
        {
            if (_circleMesh != null) return _circleMesh;
            const int seg = 32;
            _circleMesh = new Mesh { name = "VisCircle" };
            var v = new Vector3[seg + 1]; var n = new Vector3[seg + 1];
            v[0] = Vector3.zero; n[0] = Vector3.up;
            for (int i = 0; i < seg; i++)
            {
                float a = 2f * Mathf.PI * i / seg;
                v[i+1] = new Vector3(Mathf.Cos(a)*0.5f, 0, Mathf.Sin(a)*0.5f);
                n[i+1] = Vector3.up;
            }
            var t = new List<int>();
            for (int i = 0; i < seg; i++)
            { int nx = (i+1)%seg; t.Add(0); t.Add(i+1); t.Add(nx+1); t.Add(0); t.Add(nx+1); t.Add(i+1); }
            _circleMesh.vertices = v; _circleMesh.normals = n;
            _circleMesh.triangles = t.ToArray();
            _circleMesh.RecalculateBounds();
            return _circleMesh;
        }
    }
}
