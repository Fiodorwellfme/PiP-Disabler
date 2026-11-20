using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PiPDisabler
{
    public static class MeshPlaneCutter
    {
        public enum KeepSide { Positive, Negative }

        public static Mesh MakeReadableMeshCopy(Mesh nonReadableMesh)
        {
            if (nonReadableMesh == null) return null;

            // Some runtime meshes (e.g. 'MuzzleJet UpdateOrCreateMesh') have no GPU vertex buffer.
            // Bail out before allocating; a failed copy used to leak a Mesh (and GPU buffers) every rebuild.
            if (nonReadableMesh.vertexBufferCount == 0 || nonReadableMesh.vertexCount == 0)
                return null;

            Mesh meshCopy = null;
            GraphicsBuffer verticesBuffer = null;
            GraphicsBuffer indexesBuffer = null;
            bool ok = false;
            try
            {
                verticesBuffer = nonReadableMesh.GetVertexBuffer(0);
                indexesBuffer = nonReadableMesh.GetIndexBuffer();
                if (verticesBuffer == null || indexesBuffer == null)
                    return null;

                meshCopy = new Mesh();
                meshCopy.indexFormat = nonReadableMesh.indexFormat;

                // Copy vertex buffer from GPU
                int totalSize = verticesBuffer.stride * verticesBuffer.count;
                byte[] data = new byte[totalSize];
                verticesBuffer.GetData(data);
                meshCopy.SetVertexBufferParams(nonReadableMesh.vertexCount, nonReadableMesh.GetVertexAttributes());
                meshCopy.SetVertexBufferData(data, 0, 0, totalSize);

                // Copy index buffer from GPU
                meshCopy.subMeshCount = nonReadableMesh.subMeshCount;
                int tot = indexesBuffer.stride * indexesBuffer.count;
                byte[] indexesData = new byte[tot];
                indexesBuffer.GetData(indexesData);
                meshCopy.SetIndexBufferParams(indexesBuffer.count, nonReadableMesh.indexFormat);
                meshCopy.SetIndexBufferData(indexesData, 0, 0, tot);

                // Restore submesh structure
                uint currentIndexOffset = 0;
                for (int i = 0; i < meshCopy.subMeshCount; i++)
                {
                    uint subMeshIndexCount = nonReadableMesh.GetIndexCount(i);
                    meshCopy.SetSubMesh(i, new SubMeshDescriptor((int)currentIndexOffset, (int)subMeshIndexCount));
                    currentIndexOffset += subMeshIndexCount;
                }

                meshCopy.RecalculateNormals();
                meshCopy.RecalculateBounds();
                ok = true;
                return meshCopy;
            }
            finally
            {
                verticesBuffer?.Release();
                indexesBuffer?.Release();
                if (!ok && meshCopy != null)
                    UnityEngine.Object.Destroy(meshCopy);
            }
        }

        public static bool CutMeshDirect(
            Mesh mesh,
            Transform meshTransform,
            Vector3 planePointWorld,
            Vector3 planeNormalWorld,
            KeepSide keepSide,
            float epsilon = 1e-5f)
        {
            if (mesh == null) return false;

            Vector3 pL = meshTransform.InverseTransformPoint(planePointWorld);
            Vector3 nL = meshTransform.InverseTransformDirection(planeNormalWorld).normalized;

            var verts = mesh.vertices;
            var norms = mesh.normals;
            var tangs = mesh.tangents;
            var uvs   = mesh.uv;

            bool hasNormals  = norms != null && norms.Length == verts.Length;
            bool hasTangents = tangs != null && tangs.Length == verts.Length;
            bool hasUV       = uvs   != null && uvs.Length   == verts.Length;

            int subMeshCount = mesh.subMeshCount;

            var outVerts = new List<Vector3>(verts.Length);
            var outNorms = hasNormals  ? new List<Vector3>(verts.Length) : null;
            var outTangs = hasTangents ? new List<Vector4>(verts.Length) : null;
            var outUVs   = hasUV       ? new List<Vector2>(verts.Length) : null;

            var keptMap = new Dictionary<int, int>(verts.Length);

            var outTris = new List<int>[subMeshCount];
            for (int s = 0; s < subMeshCount; s++)
                outTris[s] = new List<int>(mesh.GetTriangles(s).Length);

            float SideValue(Vector3 v) => Vector3.Dot(nL, v - pL);

            int AddVertexFromOld(int oldIndex)
            {
                if (keptMap.TryGetValue(oldIndex, out int ni))
                    return ni;

                int newIndex = outVerts.Count;
                outVerts.Add(verts[oldIndex]);
                if (hasNormals) outNorms.Add(norms[oldIndex]);
                if (hasTangents) outTangs.Add(tangs[oldIndex]);
                if (hasUV) outUVs.Add(uvs[oldIndex]);
                keptMap[oldIndex] = newIndex;
                return newIndex;
            }

            int AddInterpolatedVertex(int a, int b, float t01)
            {
                int newIndex = outVerts.Count;
                outVerts.Add(Vector3.LerpUnclamped(verts[a], verts[b], t01));
                if (hasNormals)
                    outNorms.Add(Vector3.SlerpUnclamped(norms[a], norms[b], t01).normalized);
                if (hasTangents)
                    outTangs.Add(Vector4.LerpUnclamped(tangs[a], tangs[b], t01));
                if (hasUV)
                    outUVs.Add(Vector2.LerpUnclamped(uvs[a], uvs[b], t01));
                return newIndex;
            }

            bool Keep(float side) =>
                keepSide == KeepSide.Positive ? side >= -epsilon : side <= epsilon;

            for (int s = 0; s < subMeshCount; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
                    float d0 = SideValue(verts[i0]);
                    float d1 = SideValue(verts[i1]);
                    float d2 = SideValue(verts[i2]);
                    bool k0 = Keep(d0), k1 = Keep(d1), k2 = Keep(d2);
                    int keptCount = (k0 ? 1 : 0) + (k1 ? 1 : 0) + (k2 ? 1 : 0);

                    if (keptCount == 3)
                    {
                        outTris[s].Add(AddVertexFromOld(i0));
                        outTris[s].Add(AddVertexFromOld(i1));
                        outTris[s].Add(AddVertexFromOld(i2));
                    }
                    else if (keptCount == 0)
                    {
                        continue;
                    }
                    else
                    {
                        int[] idx = { i0, i1, i2 };
                        float[] d = { d0, d1, d2 };
                        bool[] k = { k0, k1, k2 };

                        float IntersectT(int a, int b)
                        {
                            float denom = d[b] - d[a];
                            if (Mathf.Abs(denom) < 1e-12f) return 0.5f;
                            return Mathf.Clamp01(-d[a] / denom);
                        }

                        if (keptCount == 1)
                        {
                            int a = k[0] ? 0 : (k[1] ? 1 : 2);
                            int b = (a + 1) % 3;
                            int c = (a + 2) % 3;

                            int va = AddVertexFromOld(idx[a]);
                            int vAB = AddInterpolatedVertex(idx[a], idx[b], IntersectT(a, b));
                            int vAC = AddInterpolatedVertex(idx[a], idx[c], IntersectT(a, c));

                            outTris[s].Add(va);
                            outTris[s].Add(vAB);
                            outTris[s].Add(vAC);
                        }
                        else
                        {
                            int a = !k[0] ? 0 : (!k[1] ? 1 : 2);
                            int b = (a + 1) % 3;
                            int c = (a + 2) % 3;

                            int vb = AddVertexFromOld(idx[b]);
                            int vc = AddVertexFromOld(idx[c]);
                            int vBA = AddInterpolatedVertex(idx[b], idx[a], IntersectT(b, a));
                            int vCA = AddInterpolatedVertex(idx[c], idx[a], IntersectT(c, a));

                            outTris[s].Add(vb);
                            outTris[s].Add(vc);
                            outTris[s].Add(vCA);

                            outTris[s].Add(vb);
                            outTris[s].Add(vCA);
                            outTris[s].Add(vBA);
                        }
                    }
                }
            }

            mesh.Clear();
            mesh.SetVertices(outVerts);
            if (hasNormals)  mesh.SetNormals(outNorms);
            if (hasTangents) mesh.SetTangents(outTangs);
            if (hasUV)       mesh.SetUVs(0, outUVs);

            mesh.subMeshCount = subMeshCount;
            for (int s = 0; s < subMeshCount; s++)
                mesh.SetTriangles(outTris[s], s, true);

            mesh.RecalculateBounds();
            if (!hasNormals) mesh.RecalculateNormals();

            return true;
        }

        // Diagnostics for the last CutMeshFrustum call (triangles).
        public static int LastTris, LastRemovedCore, LastRemovedCone, LastKeptByFacing;

        public static bool CutMeshFrustum(
            Mesh mesh,
            Transform meshTransform,
            Vector3 centerWorld,
            Vector3 axisWorld,
            float nearRadius,
            float farRadius,
            float startOffset,
            float length,
            bool keepInside,
            float midRadius = 0f,
            float midPosition = 0.5f,
            float nearPreserveDepth = 0f,
            float plane3Radius = 0f,
            float plane3Position = 0.66f,
            float plane4Position = 1f,
            float epsilon = 1e-5f,
            Vector3? eyeWorld = null,
            float coreRadius = 0f)
        {
            if (mesh == null || nearRadius <= 0 || length <= 0) return false;
            // eyeWorld set (automatic hole): inside the volume only faces that face the eye are
            // removed. Inner walls/rings face the eye and block the view; the scope's outer skin
            // faces away — seen from the eye it is a back face (culled), seen from outside it is the
            // scope's shape — so it stays even where the hole is wider than the tube.
            bool facingTest = eyeWorld.HasValue;
            Vector3 eyeL = facingTest ? meshTransform.InverseTransformPoint(eyeWorld.Value) : Vector3.zero;
            bool flipWinding = facingTest && meshTransform.localToWorldMatrix.determinant < 0f;
            // Core = the straight lens-sized tube in front of the eyepiece. Everything there goes,
            // whichever way it faces: inner parts of some scopes are two-sided or wound the other
            // way, and keeping "away-facing" ones there blacked out the whole lens (Razor, 2.7.1).
            float avgScaleCore = (Mathf.Abs(meshTransform.lossyScale.x) + Mathf.Abs(meshTransform.lossyScale.y) + Mathf.Abs(meshTransform.lossyScale.z)) / 3f;
            float localCoreR = coreRadius > 0f ? (avgScaleCore > 0.001f ? coreRadius / avgScaleCore : coreRadius) : 0f;
            if (farRadius <= 0) farRadius = nearRadius;
            Vector3 cL = meshTransform.InverseTransformPoint(centerWorld);
            Vector3 aL = meshTransform.InverseTransformDirection(axisWorld).normalized;

            Vector3 lossyScale = meshTransform.lossyScale;
            float avgScale = (Mathf.Abs(lossyScale.x) + Mathf.Abs(lossyScale.y) + Mathf.Abs(lossyScale.z)) / 3f;
            float localNearR = avgScale > 0.001f ? nearRadius / avgScale : nearRadius;
            float localFarR  = avgScale > 0.001f ? farRadius / avgScale : farRadius;
            float localMidR  = midRadius > 0f ? (avgScale > 0.001f ? midRadius / avgScale : midRadius) : 0f;
            float localStart = avgScale > 0.001f ? startOffset / avgScale : startOffset;
            float localLen   = avgScale > 0.001f ? length / avgScale : length;
            float localMidPos = Mathf.Clamp01(midPosition);
            float localP3Pos = Mathf.Clamp01(plane3Position);
            float localP4Pos = Mathf.Clamp01(plane4Position);
            float localP3R = plane3Radius > 0f ? (avgScale > 0.001f ? plane3Radius / avgScale : plane3Radius) : 0f;
            float localPreserve = nearPreserveDepth > 0f ? (avgScale > 0.001f ? nearPreserveDepth / avgScale : nearPreserveDepth) : 0f;
            float _cNearR = localNearR, _cFarR = localFarR, _cMidR = localMidR;
            float _cMidPos = localMidPos, _cP3R = localP3R, _cP3Pos = localP3Pos, _cP4Pos = localP4Pos;
            float _cStart = localStart, _cLen = localLen;
            float _cPreserve = localPreserve;

            var verts = mesh.vertices;
            var norms = mesh.normals;
            var tangs = mesh.tangents;
            var uvs   = mesh.uv;

            bool hasNormals  = norms != null && norms.Length == verts.Length;
            bool hasTangents = tangs != null && tangs.Length == verts.Length;
            bool hasUV       = uvs   != null && uvs.Length   == verts.Length;

            int subMeshCount = mesh.subMeshCount;

            var outVerts = new List<Vector3>(verts.Length);
            var outNorms = hasNormals  ? new List<Vector3>(verts.Length) : null;
            var outTangs = hasTangents ? new List<Vector4>(verts.Length) : null;
            var outUVs   = hasUV       ? new List<Vector2>(verts.Length) : null;

            var keptMap = new Dictionary<int, int>(verts.Length);

            var outTris = new List<int>[subMeshCount];
            for (int s = 0; s < subMeshCount; s++)
                outTris[s] = new List<int>(mesh.GetTriangles(s).Length);

            bool IsInsideFrustum(Vector3 v)
            {
                Vector3 diff = v - cL;
                float axialDist = Vector3.Dot(diff, aL);
                float cutStart = -_cStart;
                float cutEnd = cutStart + _cLen;

                if (axialDist < cutStart - epsilon || axialDist > cutEnd + epsilon)
                    return false;
                if (_cPreserve > 0f && axialDist < cutStart + _cPreserve)
                    return false;
                float t = (_cLen > epsilon) ? Mathf.Clamp01((axialDist - cutStart) / _cLen) : 0f;
                float radiusAtDepth = RadiusAtT4(t, _cNearR, _cMidR, _cMidPos, _cP3R, _cP3Pos, _cFarR, _cP4Pos);

                Vector3 projected = axialDist * aL;
                float perpDist = (diff - projected).magnitude;

                return perpDist <= radiusAtDepth + epsilon;
            }

            bool IsInCore(Vector3 v)
            {
                if (localCoreR <= 0f) return false;
                Vector3 diff = v - cL;
                float axialDist = Vector3.Dot(diff, aL);
                // Whole cut length (behind and in front of the lens), minus the eyepiece ring kept
                // next to the eye. 2.7.2 only covered the part in front of the lens and a reflective
                // disc right at the lens plane stayed and filled the Razor's lens.
                float coreStart = -_cStart + (_cPreserve > 0f ? _cPreserve : 0f);
                if (axialDist < coreStart - epsilon || axialDist > -_cStart + _cLen + epsilon) return false;
                Vector3 projected = axialDist * aL;
                return (diff - projected).magnitude <= localCoreR;
            }

            int AddVertexFromOld(int oldIndex)
            {
                if (keptMap.TryGetValue(oldIndex, out int ni))
                    return ni;
                int newIndex = outVerts.Count;
                outVerts.Add(verts[oldIndex]);
                if (hasNormals) outNorms.Add(norms[oldIndex]);
                if (hasTangents) outTangs.Add(tangs[oldIndex]);
                if (hasUV) outUVs.Add(uvs[oldIndex]);
                keptMap[oldIndex] = newIndex;
                return newIndex;
            }

            LastTris = 0; LastRemovedCore = 0; LastRemovedCone = 0; LastKeptByFacing = 0;
            for (int s = 0; s < subMeshCount; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    LastTris++;
                    int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
                    bool in0 = IsInsideFrustum(verts[i0]);
                    bool in1 = IsInsideFrustum(verts[i1]);
                    bool in2 = IsInsideFrustum(verts[i2]);

                    bool keepTri;
                    if (keepInside)
                        keepTri = in0 || in1 || in2; // keep if any vertex inside
                    else
                        keepTri = !in0 && !in1 && !in2;

                    bool inCore = !keepTri && facingTest
                        && (IsInCore(verts[i0]) || IsInCore(verts[i1]) || IsInCore(verts[i2]));
                    if (!keepTri && facingTest && !inCore)
                    {
                        Vector3 v0 = verts[i0], v1 = verts[i1], v2 = verts[i2];
                        Vector3 n = Vector3.Cross(v1 - v0, v2 - v0);
                        if (flipWinding) n = -n;
                        Vector3 c = (v0 + v1 + v2) * (1f / 3f);
                        if (Vector3.Dot(n, eyeL - c) <= 0f)
                        {
                            keepTri = true; // faces away from the eye: keep (outer skin)
                            LastKeptByFacing++;
                        }
                    }
                    if (!keepTri)
                    {
                        if (inCore) LastRemovedCore++;
                        else LastRemovedCone++;
                    }
                    if (keepTri)
                    {
                        outTris[s].Add(AddVertexFromOld(i0));
                        outTris[s].Add(AddVertexFromOld(i1));
                        outTris[s].Add(AddVertexFromOld(i2));
                    }
                }
            }

            long triCount = 0;
            for (int s = 0; s < subMeshCount; s++) triCount += outTris[s].Count;
            if (triCount == 0) return false;

            mesh.Clear();
            mesh.SetVertices(outVerts);
            if (hasNormals)  mesh.SetNormals(outNorms);
            if (hasTangents) mesh.SetTangents(outTangs);
            if (hasUV)       mesh.SetUVs(0, outUVs);

            mesh.subMeshCount = subMeshCount;
            for (int s = 0; s < subMeshCount; s++)
                mesh.SetTriangles(outTris[s], s, true);

            mesh.RecalculateBounds();
            if (!hasNormals) mesh.RecalculateNormals();

            return true;
        }


        public static float RadiusAtT4(float t, float plane1R, float plane2R, float plane2Pos,
            float plane3R, float plane3Pos, float plane4R, float plane4Pos)
        {
            t = Mathf.Clamp01(t);

            float p1 = 0f;
            float p2 = Mathf.Clamp01(plane2Pos);
            float p3 = Mathf.Clamp01(plane3Pos);
            float p4 = Mathf.Clamp01(plane4Pos);

            float r1 = Mathf.Max(0f, plane1R);
            float r2 = Mathf.Max(0f, plane2R);
            float r3 = plane3R > 0f ? Mathf.Max(0f, plane3R) : r2;
            float r4 = Mathf.Max(0f, plane4R);

            if (p2 < p1) p2 = p1;
            if (p3 < p2) p3 = p2;
            if (p4 < p3) p4 = p3;

            if (t <= p2)
            {
                float seg = p2 > 1e-5f ? t / p2 : 0f;
                return Mathf.Lerp(r1, r2, seg);
            }
            if (t <= p3)
            {
                float denom = p3 - p2;
                float seg = denom > 1e-5f ? (t - p2) / denom : 0f;
                return Mathf.Lerp(r2, r3, seg);
            }
            {
                float denom = p4 - p3;
                float seg = denom > 1e-5f ? (t - p3) / denom : 1f;
                return Mathf.Lerp(r3, r4, seg);
            }
        }

        public static float RadiusAtT(float t, float nearR, float midR, float midPos, float farR)
        {
            return RadiusAtT4(t, nearR, midR, midPos, 0f, midPos, farR, 1f);
        }

        public static bool CutMeshCylinder(
            Mesh mesh,
            Transform meshTransform,
            Vector3 centerWorld,
            Vector3 axisWorld,
            float radius,
            bool keepInside,
            float epsilon = 1e-5f)
        {
            return CutMeshFrustum(mesh, meshTransform, centerWorld, axisWorld,
                radius, radius, 0f, 999f, keepInside,
                midRadius: 0f, midPosition: 0.5f, nearPreserveDepth: 0f, epsilon: epsilon);
        }
        // ── Automatic hole (2.7.6): remove what the camera sees through the lens ──
        // Diagnostics for the last CutMeshSightCone call.
        public static int LastSightRemoved, LastSightSplit;
        [ThreadStatic] private static List<SVert> _clipPolyTs;
        private static List<SVert> _clipPoly => _clipPolyTs ?? (_clipPolyTs = new List<SVert>(4));

        private struct SVert
        {
            public Vector3 P, N; public Vector4 T; public Vector2 UV;
            public static SVert Mid(in SVert a, in SVert b) => new SVert
            {
                P = (a.P + b.P) * 0.5f,
                N = ((a.N + b.N) * 0.5f).normalized,
                T = (a.T + b.T) * 0.5f,
                UV = (a.UV + b.UV) * 0.5f
            };
            public static SVert Lerp(in SVert a, in SVert b, float t) => new SVert
            {
                P = Vector3.LerpUnclamped(a.P, b.P, t),
                N = Vector3.LerpUnclamped(a.N, b.N, t).normalized,
                T = Vector4.LerpUnclamped(a.T, b.T, t),
                UV = Vector2.LerpUnclamped(a.UV, b.UV, t)
            };
        }

        /// <summary>
        /// Removes every part of the mesh that the camera would see through the eyepiece lens.
        ///
        /// Each point is projected from the camera (on the lens axis, <paramref name="eyeDistance"/>
        /// behind the lens) onto the lens plane. Whatever lands inside the lens circle sits in the
        /// line of sight through the lens and is removed, whichever way it faces and whether it is
        /// in front of or behind the lens (inner walls, two-sided outer skin, eyepiece glass baked
        /// into the body). Whatever lands outside is exactly what is seen around the lens and is
        /// kept, so the scope's outside does not change on screen. Triangles crossing the circle are
        /// split so the edge follows the circle instead of cutting whole big faces.
        /// Limit: 0.97 × lens radius up to the lens plane (keeps the eyepiece bore and rim), easing
        /// to 1.0 × by 2 cm past it, all × <paramref name="widthScale"/>. (2.7.6 used 1.05: on the
        /// Razor that cut parts of the offset red-dot mount visible around the thin eyepiece.)
        /// </summary>
        public static bool CutMeshSightCone(Mesh mesh, Transform meshTransform,
            Vector3 lensCenterWorld, Vector3 lensNormalWorld, float eyeDistance,
            float lensRadius, float widthScale, Matrix4x4? worldPre = null,
            float offAxisMargin = 0f, float[] housingCap = null)
        {
            LastTris = 0; LastSightRemoved = 0; LastSightSplit = 0;
            LastRemovedCore = 0; LastRemovedCone = 0; LastKeptByFacing = 0;
            if (mesh == null || eyeDistance <= 0.02f || lensRadius <= 0f) return false;

            Vector3 n = lensNormalWorld.normalized;
            Vector3 u = Vector3.Cross(n, Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 v = Vector3.Cross(n, u);
            // worldPre: extra world transform (predicted weapon stretch for a cut made ahead).
            Matrix4x4 l2w = worldPre.HasValue ? worldPre.Value * meshTransform.localToWorldMatrix : meshTransform.localToWorldMatrix;
            float baseLimit = lensRadius * Mathf.Max(0.05f, widthScale);

            // Projected position on the lens plane (lens units = metres there) and depth along the axis.
            bool Project(Vector3 local, out Vector2 p, out float x)
            {
                Vector3 d = l2w.MultiplyPoint3x4(local) - lensCenterWorld;
                x = Vector3.Dot(d, n);
                float denom = eyeDistance + x;
                if (denom < 0.02f) { p = default; return false; } // at/behind the camera: never in sight
                float s = eyeDistance / denom;
                p = new Vector2(Vector3.Dot(d, u) * s, Vector3.Dot(d, v) * s);
                return true;
            }
            float LimitAt(float x)
            {
                if (x <= 0f) return baseLimit * 0.97f;
                return baseLimit * Mathf.Lerp(0.97f, 1.0f, Mathf.Clamp01(x / 0.02f));
            }
            // Off-axis margin (2.8.1): recoil/sway move the camera a few mm off the lens axis; parts
            // in front of the lens then shift into view by margin·x/(eye+x). Cut that much wider,
            // but never past the eyepiece housing's outline on screen (housingCap per sector), so on
            // axis the extra cut stays hidden behind the housing.
            bool useMargin = offAxisMargin > 0f && housingCap != null && housingCap.Length > 0;
            float capMax = 0f;
            if (useMargin) foreach (var c0 in housingCap) capMax = Mathf.Max(capMax, c0);
            // Interpolated between sector centres (2.8.4): a step per sector made the hole edge jagged.
            float CapAt(Vector2 p)
            {
                int n0 = housingCap.Length;
                float f = (Mathf.Atan2(p.y, p.x) + Mathf.PI) / (2f * Mathf.PI) * n0 - 0.5f;
                int i0 = Mathf.FloorToInt(f);
                float t = f - i0;
                int a0 = ((i0 % n0) + n0) % n0, b0 = (a0 + 1) % n0;
                return Mathf.Lerp(housingCap[a0], housingCap[b0], t) * 0.98f;
            }
            float LimitAtP(Vector2 p, float x)
            {
                float b = LimitAt(x);
                if (!useMargin || x <= 0f) return b;
                return Mathf.Max(b, Mathf.Min(b + offAxisMargin * x / (eyeDistance + x), CapAt(p)));
            }
            float LimitMax(float x)
            {
                float b = LimitAt(x);
                if (!useMargin || x <= 0f) return b;
                return Mathf.Max(b, Mathf.Min(b + offAxisMargin * x / (eyeDistance + x), capMax * 0.98f));
            }

            var verts = mesh.vertices;
            var norms = mesh.normals;
            var tangs = mesh.tangents;
            var uvs = mesh.uv;
            bool hasN = norms != null && norms.Length == verts.Length;
            bool hasT = tangs != null && tangs.Length == verts.Length;
            bool hasUV = uvs != null && uvs.Length == verts.Length;

            int vc = verts.Length;
            var pr = new Vector2[vc];
            var px = new float[vc];
            var pok = new bool[vc];
            for (int i = 0; i < vc; i++) pok[i] = Project(verts[i], out pr[i], out px[i]);

            var outV = new List<Vector3>(vc);
            var outN = hasN ? new List<Vector3>(vc) : null;
            var outT = hasT ? new List<Vector4>(vc) : null;
            var outUV = hasUV ? new List<Vector2>(vc) : null;
            var keptMap = new Dictionary<int, int>(vc);

            int AddOld(int i)
            {
                if (keptMap.TryGetValue(i, out int ni)) return ni;
                ni = outV.Count;
                outV.Add(verts[i]);
                if (hasN) outN.Add(norms[i]);
                if (hasT) outT.Add(tangs[i]);
                if (hasUV) outUV.Add(uvs[i]);
                keptMap[i] = ni;
                return ni;
            }
            int AddNew(in SVert s)
            {
                int ni = outV.Count;
                outV.Add(s.P);
                if (hasN) outN.Add(s.N);
                if (hasT) outT.Add(s.T);
                if (hasUV) outUV.Add(s.UV);
                return ni;
            }
            SVert Get(int i) => new SVert
            {
                P = verts[i],
                N = hasN ? norms[i] : Vector3.zero,
                T = hasT ? tangs[i] : Vector4.zero,
                UV = hasUV ? uvs[i] : Vector2.zero
            };

            // Smallest distance from the lens centre to the projected triangle (0 if it covers it).
            float DistToOrigin(Vector2 a, Vector2 b, Vector2 c)
            {
                float d1 = Cross2(b - a, -a), d2 = Cross2(c - b, -b), d3 = Cross2(a - c, -c);
                bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                if (!(neg && pos)) return 0f;
                return Mathf.Min(SegDist(a, b), Mathf.Min(SegDist(b, c), SegDist(c, a)));
            }

            int subCount = mesh.subMeshCount;
            var outTris = new List<int>[subCount];
            for (int s = 0; s < subCount; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                var list = outTris[s] = new List<int>(tris.Length);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    LastTris++;
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    if (!pok[i0] || !pok[i1] || !pok[i2])
                    {
                        list.Add(AddOld(i0)); list.Add(AddOld(i1)); list.Add(AddOld(i2));
                        continue;
                    }
                    bool in0 = pr[i0].magnitude < LimitAtP(pr[i0], px[i0]);
                    bool in1 = pr[i1].magnitude < LimitAtP(pr[i1], px[i1]);
                    bool in2 = pr[i2].magnitude < LimitAtP(pr[i2], px[i2]);
                    if (in0 && in1 && in2) { LastSightRemoved++; continue; }
                    // A projected triangle is exactly the triangle of the projected corners (central
                    // projection keeps lines straight), so this test is exact.
                    if (!in0 && !in1 && !in2 && DistToOrigin(pr[i0], pr[i1], pr[i2]) >= Mathf.Max(LimitMax(px[i0]), Mathf.Max(LimitMax(px[i1]), LimitMax(px[i2]))))
                    {
                        list.Add(AddOld(i0)); list.Add(AddOld(i1)); list.Add(AddOld(i2));
                        continue;
                    }

                    // Crosses the lens circle: split it, then keep/remove the pieces.
                    LastSightSplit++;
                    float size = Mathf.Max((pr[i0] - pr[i1]).magnitude,
                        Mathf.Max((pr[i1] - pr[i2]).magnitude, (pr[i2] - pr[i0]).magnitude));
                    int depth = size < 0.08f * lensRadius ? 0 : size < 0.25f * lensRadius ? 2 : size < 0.7f * lensRadius ? 3 : size < 2f * lensRadius ? 4 : 5;
                    if (depth == 0 && !in0 && !in1 && !in2)
                    {
                        Vector2 c = (pr[i0] + pr[i1] + pr[i2]) / 3f;
                        if (c.magnitude < LimitAtP(c, (px[i0] + px[i1] + px[i2]) / 3f)) { LastSightRemoved++; continue; }
                        list.Add(AddOld(i0)); list.Add(AddOld(i1)); list.Add(AddOld(i2));
                        continue;
                    }
                    Split(Get(i0), Get(i1), Get(i2), depth, list);
                }
            }

            void Split(in SVert a, in SVert b, in SVert c, int depth, List<int> list)
            {
                if (depth <= 0)
                {
                    EmitClipped(a, b, c, list);
                    return;
                }
                // A piece wholly outside (or wholly inside) needs no further splitting.
                if (Project(a.P, out var pa, out var xa) && Project(b.P, out var pb, out var xb) && Project(c.P, out var pcc, out var xcc))
                {
                    bool ia = pa.magnitude < LimitAtP(pa, xa), ib = pb.magnitude < LimitAtP(pb, xb), ic = pcc.magnitude < LimitAtP(pcc, xcc);
                    if (ia && ib && ic) return;
                    if (!ia && !ib && !ic && DistToOrigin(pa, pb, pcc) >= Mathf.Max(LimitMax(xa), Mathf.Max(LimitMax(xb), LimitMax(xcc))))
                    {
                        list.Add(AddNew(a)); list.Add(AddNew(b)); list.Add(AddNew(c));
                        return;
                    }
                }
                SVert ab = SVert.Mid(a, b), bc = SVert.Mid(b, c), ca = SVert.Mid(c, a);
                Split(a, ab, ca, depth - 1, list);
                Split(ab, b, bc, depth - 1, list);
                Split(ca, bc, c, depth - 1, list);
                Split(ab, bc, ca, depth - 1, list);
            }

            // >= 0: outside the line of sight (kept), < 0: inside (removed).
            float Keep(in SVert q)
            {
                if (!Project(q.P, out var pq, out var xq)) return 1f; // at/behind the camera
                return pq.magnitude - LimitAtP(pq, xq);
            }
            // Point on the edge where it leaves the line of sight (bisection along the 3D edge; the
            // projection of a segment is a segment, so there is one crossing). Always searched from
            // the kept end, so two pieces sharing the edge get the very same point (no crack).
            SVert Crossing(in SVert keep, in SVert cut)
            {
                float lo = 0f, hi = 1f;
                for (int it = 0; it < 12; it++)
                {
                    float m = (lo + hi) * 0.5f;
                    if (Keep(SVert.Lerp(keep, cut, m)) >= 0f) lo = m; else hi = m;
                }
                return SVert.Lerp(keep, cut, lo);
            }
            // Smallest piece (2.8.4): clip it on the lens edge instead of keeping/removing it whole by
            // its centre. Whole pieces left a saw-tooth edge that looked jagged when zoomed in.
            void EmitClipped(in SVert a, in SVert b, in SVert c, List<int> list)
            {
                float fa = Keep(a), fb = Keep(b), fc = Keep(c);
                bool ka = fa >= 0f, kb = fb >= 0f, kc = fc >= 0f;
                if (!ka && !kb && !kc) return;
                if (ka && kb && kc)
                {
                    Vector3 centre = (a.P + b.P + c.P) / 3f;
                    if (Project(centre, out Vector2 pc, out float xc) && pc.magnitude < LimitAtP(pc, xc)) return;
                    list.Add(AddNew(a)); list.Add(AddNew(b)); list.Add(AddNew(c));
                    return;
                }
                // Keep side of the triangle (Sutherland–Hodgman against the lens edge), same winding.
                var poly = _clipPoly;
                poly.Clear();
                if (ka) poly.Add(a);
                if (ka != kb) poly.Add(ka ? Crossing(a, b) : Crossing(b, a));
                if (kb) poly.Add(b);
                if (kb != kc) poly.Add(kb ? Crossing(b, c) : Crossing(c, b));
                if (kc) poly.Add(c);
                if (kc != ka) poly.Add(kc ? Crossing(c, a) : Crossing(a, c));
                if (poly.Count < 3) return;
                int first = AddNew(poly[0]);
                int prev = AddNew(poly[1]);
                for (int k = 2; k < poly.Count; k++)
                {
                    int cur = AddNew(poly[k]);
                    list.Add(first); list.Add(prev); list.Add(cur);
                    prev = cur;
                }
            }

            long kept = 0;
            for (int s = 0; s < subCount; s++) kept += outTris[s].Count;
            LastRemovedCore = LastSightRemoved;
            if (kept == 0) return false;

            mesh.Clear();
            if (outV.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(outV);
            if (hasN) mesh.SetNormals(outN);
            if (hasT) mesh.SetTangents(outT);
            if (hasUV) mesh.SetUVs(0, outUV);
            mesh.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++)
                mesh.SetTriangles(outTris[s], s, true);
            mesh.RecalculateBounds();
            if (!hasN) mesh.RecalculateNormals();
            return true;
        }

        /// <summary>
        /// Outline of the eyepiece housing as seen from the camera (2.8.1): for each of
        /// <paramref name="sectors"/> directions around the lens, the farthest projected radius
        /// (on the lens plane) of housing geometry near the eyepiece (−40 mm … +10 mm along the
        /// axis, between 0.9× and 2× the lens radius). Smoothed with the neighbours' minimum so a
        /// thin lever does not count as a solid housing. Same projection as CutMeshSightCone.
        /// </summary>
        public static float[] ComputeHousingOutline(IEnumerable<Vector3> worldVerts, Vector3 lensCenterWorld,
            Vector3 lensNormalWorld, float eyeDistance, float lensRadius, int sectors = 32)
        {
            Vector3 n = lensNormalWorld.normalized;
            Vector3 u = Vector3.Cross(n, Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 v = Vector3.Cross(n, u);
            var max = new float[sectors];
            foreach (var w in worldVerts)
            {
                Vector3 d = w - lensCenterWorld;
                float x = Vector3.Dot(d, n);
                if (x < -0.04f || x > 0.01f) continue;
                float denom = eyeDistance + x;
                if (denom < 0.02f) continue;
                float sc = eyeDistance / denom;
                var p = new Vector2(Vector3.Dot(d, u) * sc, Vector3.Dot(d, v) * sc);
                float r = p.magnitude;
                if (r < lensRadius * 0.9f || r > lensRadius * 2f) continue;
                int si = Mathf.Clamp((int)((Mathf.Atan2(p.y, p.x) + Mathf.PI) / (2f * Mathf.PI) * sectors), 0, sectors - 1);
                if (r > max[si]) max[si] = r;
            }
            var outl = new float[sectors];
            for (int i = 0; i < sectors; i++)
            {
                float a = max[(i + sectors - 1) % sectors], b = max[i], c = max[(i + 1) % sectors];
                float m = Mathf.Min(a, Mathf.Min(b, c));
                outl[i] = Mathf.Max(m, lensRadius); // no housing found → no extra cut there
            }
            return outl;
        }

        private static float Cross2(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static float SegDist(Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            float t = l2 > 1e-12f ? Mathf.Clamp01(-Vector2.Dot(a, ab) / l2) : 0f;
            return (a + ab * t).magnitude;
        }

    }
}
