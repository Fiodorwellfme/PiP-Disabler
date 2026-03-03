using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScopeHousingMeshSurgery
{
    public static class MeshPlaneCutter
    {
        public enum KeepSide { Positive, Negative }

        /// <summary>
        /// Creates a CPU-readable copy of a non-readable mesh by copying data
        /// directly from the GPU vertex/index buffers.
        /// Works in Unity 2021+ (EFT uses Unity 2021.x).
        /// </summary>
        public static Mesh MakeReadableMeshCopy(Mesh nonReadableMesh)
        {
            if (nonReadableMesh == null) return null;

            Mesh meshCopy = new Mesh();
            meshCopy.indexFormat = nonReadableMesh.indexFormat;

            // Copy vertex buffer from GPU
            GraphicsBuffer verticesBuffer = nonReadableMesh.GetVertexBuffer(0);
            int totalSize = verticesBuffer.stride * verticesBuffer.count;
            byte[] data = new byte[totalSize];
            verticesBuffer.GetData(data);
            meshCopy.SetVertexBufferParams(nonReadableMesh.vertexCount, nonReadableMesh.GetVertexAttributes());
            meshCopy.SetVertexBufferData(data, 0, 0, totalSize);
            verticesBuffer.Release();

            // Copy index buffer from GPU
            meshCopy.subMeshCount = nonReadableMesh.subMeshCount;
            GraphicsBuffer indexesBuffer = nonReadableMesh.GetIndexBuffer();
            int tot = indexesBuffer.stride * indexesBuffer.count;
            byte[] indexesData = new byte[tot];
            indexesBuffer.GetData(indexesData);
            meshCopy.SetIndexBufferParams(indexesBuffer.count, nonReadableMesh.indexFormat);
            meshCopy.SetIndexBufferData(indexesData, 0, 0, tot);
            indexesBuffer.Release();

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

            return meshCopy;
        }

        /// <summary>
        /// Cuts a mesh in-place: removes all triangles (and their vertices) that
        /// lie on the discarded side of the plane. Handles edge-intersecting triangles
        /// by clipping them to the plane.
        ///
        /// Returns true if any geometry survived, false if the mesh is now empty.
        ///
        /// The mesh MUST be CPU-readable (isReadable=true). Use MakeReadableMeshCopy first
        /// if the source mesh is not readable.
        ///
        /// Does NOT instantiate any new Mesh objects — modifies the given mesh directly.
        /// </summary>
        public static bool CutMeshDirect(
            Mesh mesh,
            Transform meshTransform,
            Vector3 planePointWorld,
            Vector3 planeNormalWorld,
            KeepSide keepSide,
            float epsilon = 1e-5f)
        {
            if (mesh == null) return false;

            // Transform plane to mesh-local space
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
                        else // keptCount == 2
                        {
                            int a = !k[0] ? 0 : (!k[1] ? 1 : 2); // the discarded vertex
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

            // Check if anything survived
            long triCount = 0;
            for (int s = 0; s < subMeshCount; s++) triCount += outTris[s].Count;
            if (triCount == 0) return false;

            // Write results back into the SAME mesh
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

        /// <summary>
        /// Frustum/cone cut: removes all triangles inside a frustum (truncated cone)
        /// defined by center, axis, near radius, far radius, and depth range.
        ///
        /// nearRadius = radius at startOffset (closest to camera, plane 1)
        /// farRadius  = radius at plane 4 position
        ///   If nearRadius == farRadius → pure cylinder
        ///   If different → cone/frustum shape
        ///
        /// midRadius > 0 enables a two-segment profile: near→mid, then mid→far
        ///   midPosition = fractional position along the bore (0=near, 1=far) for plane 2
        ///   This allows hourglass (mid &lt; near/far) or bulge (mid &gt; near/far) shapes
        ///
        /// plane3Radius = near radius of frustum 2 (at the same position as plane 2)
        /// plane4Position = position of plane 4 — far end of frustum 2, near end of frustum 3
        /// plane5Radius = near radius of frustum 3 (at the same position as plane 4)
        /// plane6Radius/Position = far end of frustum 3
        ///
        /// The three frustums share boundaries: frustum 2 starts where frustum 1 ends (at
        /// midPosition), and frustum 3 starts where frustum 2 ends (at plane4Position).
        /// Planes 3 and 5 have no independent position — they are always at midPosition
        /// and plane4Position respectively, allowing a radius step at each boundary.
        ///
        /// startOffset = distance along axis from center to start of cut (toward camera = positive)
        /// length = depth of the cut volume
        ///
        /// keepInside=false → removes geometry inside any of the three frustums (bore a hole)
        /// </summary>
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
            float plane4Position = 1f,
            float plane5Radius = 0f,
            float plane6Radius = 0f,
            float plane6Position = 1f,
            float epsilon = 1e-5f)
        {
            if (mesh == null || nearRadius <= 0 || length <= 0) return false;
            if (farRadius <= 0) farRadius = nearRadius; // default to cylinder

            // Transform to mesh-local space
            Vector3 cL = meshTransform.InverseTransformPoint(centerWorld);
            Vector3 aL = meshTransform.InverseTransformDirection(axisWorld).normalized;

            // Account for non-uniform scale on radius
            Vector3 lossyScale = meshTransform.lossyScale;
            float avgScale = (Mathf.Abs(lossyScale.x) + Mathf.Abs(lossyScale.y) + Mathf.Abs(lossyScale.z)) / 3f;
            float localNearR = avgScale > 0.001f ? nearRadius / avgScale : nearRadius;
            float localFarR  = avgScale > 0.001f ? farRadius / avgScale : farRadius;
            float localMidR  = midRadius > 0f ? (avgScale > 0.001f ? midRadius / avgScale : midRadius) : 0f;
            float localStart = avgScale > 0.001f ? startOffset / avgScale : startOffset;
            float localLen   = avgScale > 0.001f ? length / avgScale : length;
            float localMidPos = Mathf.Clamp01(midPosition);
            // Plane 3 is always at the same position as plane 2 (midPosition)
            float localP3Pos = localMidPos;
            float localP4Pos = Mathf.Clamp01(plane4Position);
            float localP3R = plane3Radius > 0f ? (avgScale > 0.001f ? plane3Radius / avgScale : plane3Radius) : 0f;
            float localPreserve = nearPreserveDepth > 0f
                ? (avgScale > 0.001f ? nearPreserveDepth / avgScale : nearPreserveDepth)
                : 0f;
            float localP5R = plane5Radius > 0f ? (avgScale > 0.001f ? plane5Radius / avgScale : plane5Radius) : 0f;
            // Plane 5 is always at the same position as plane 4
            float localP5Pos = localP4Pos;
            float localP6R = plane6Radius > 0f ? (avgScale > 0.001f ? plane6Radius / avgScale : plane6Radius) : 0f;
            float localP6Pos = Mathf.Clamp01(plane6Position);

            // Log the radius profile so we can verify mid-radius is being applied
            if (midRadius > 0f)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[MeshCutter] Radius profile for '{mesh.name}': " +
                    $"localNear={localNearR:F5} localMid={localMidR:F5}@{localMidPos:F2} localFar={localFarR:F5} " +
                    $"avgScale={avgScale:F4} localLen={localLen:F4}");
                // Sample the profile at 5 points for visual verification
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[MeshCutter] Profile samples: " +
                    $"t=0.0→r={RadiusAtT6(0f, localNearR, localMidR, localMidPos, localP3R, localP3Pos, localFarR, localP4Pos, localP5R, localP5Pos, localP6R, localP6Pos):F5} " +
                    $"t=0.25→r={RadiusAtT6(0.25f, localNearR, localMidR, localMidPos, localP3R, localP3Pos, localFarR, localP4Pos, localP5R, localP5Pos, localP6R, localP6Pos):F5} " +
                    $"t=0.5→r={RadiusAtT6(0.5f, localNearR, localMidR, localMidPos, localP3R, localP3Pos, localFarR, localP4Pos, localP5R, localP5Pos, localP6R, localP6Pos):F5} " +
                    $"t=0.75→r={RadiusAtT6(0.75f, localNearR, localMidR, localMidPos, localP3R, localP3Pos, localFarR, localP4Pos, localP5R, localP5Pos, localP6R, localP6Pos):F5} " +
                    $"t=1.0→r={RadiusAtT6(1f, localNearR, localMidR, localMidPos, localP3R, localP3Pos, localFarR, localP4Pos, localP5R, localP5Pos, localP6R, localP6Pos):F5}");
            }

            // Capture values explicitly for the local function (avoid any closure ambiguity)
            float _cNearR = localNearR, _cFarR = localFarR, _cMidR = localMidR;
            float _cMidPos = localMidPos, _cP3R = localP3R, _cP3Pos = localP3Pos, _cP4Pos = localP4Pos;
            float _cP5R = localP5R, _cP5Pos = localP5Pos, _cP6R = localP6R, _cP6Pos = localP6Pos;
            float _cStart = localStart, _cLen = localLen;
            float _cPreserve = localPreserve;

            // Start point is offset along axis from center (toward camera = negative axis dir in most setups)
            // We define: axial position 0 = center, positive = along planeNormal (away from camera)
            // startOffset pushes start toward camera (positive = toward camera)
            // So the cut region is from (-startOffset) to (-startOffset + length) along axis

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

            // Test if vertex is inside the frustum
            bool IsInsideFrustum(Vector3 v)
            {
                Vector3 diff = v - cL;
                float axialDist = Vector3.Dot(diff, aL); // signed distance along axis

                // Check depth bounds: cut region is from -_cStart to -_cStart + _cLen
                float cutStart = -_cStart;
                float cutEnd = cutStart + _cLen;

                if (axialDist < cutStart - epsilon || axialDist > cutEnd + epsilon)
                    return false; // Outside depth range

                // Near preserve zone: geometry within this depth from the near plane
                // is never cut — preserves the eyepiece housing closest to the camera.
                if (_cPreserve > 0f && axialDist < cutStart + _cPreserve)
                    return false;

                // Interpolate radius based on axial position within the cut.
                float t = (_cLen > epsilon) ? Mathf.Clamp01((axialDist - cutStart) / _cLen) : 0f;
                float radiusAtDepth = RadiusAtT6(t, _cNearR, _cMidR, _cMidPos, _cP3R, _cP3Pos, _cFarR, _cP4Pos, _cP5R, _cP5Pos, _cP6R, _cP6Pos);

                // Perpendicular distance from axis
                Vector3 projected = axialDist * aL;
                float perpDist = (diff - projected).magnitude;

                return perpDist <= radiusAtDepth + epsilon;
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

            for (int s = 0; s < subMeshCount; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
                    bool in0 = IsInsideFrustum(verts[i0]);
                    bool in1 = IsInsideFrustum(verts[i1]);
                    bool in2 = IsInsideFrustum(verts[i2]);

                    bool keepTri;
                    if (keepInside)
                        keepTri = in0 || in1 || in2; // keep if any vertex inside
                    else
                        keepTri = !in0 && !in1 && !in2; // keep only if ALL vertices are outside
                        // Note: aggressive removal — boundary triangles are removed entirely.
                        // This creates a cleaner bore hole at the cost of slightly rougher edges,
                        // which the scope vignette overlay effectively hides.

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


        /// <summary>
        /// Compute radius at normalized position t (0=near, 1=far) using 6-point piecewise
        /// linear interpolation. Plane 1 is always at t=0 (closest to camera).
        /// Planes 2–6 are at configurable positions along the bore.
        /// If a plane's radius is 0, it falls back to the previous plane's radius.
        /// </summary>
        public static float RadiusAtT6(float t,
            float p1R,
            float p2R, float p2Pos,
            float p3R, float p3Pos,
            float p4R, float p4Pos,
            float p5R, float p5Pos,
            float p6R, float p6Pos)
        {
            t = Mathf.Clamp01(t);

            float p2 = Mathf.Clamp01(p2Pos);
            float p3 = Mathf.Clamp01(p3Pos);
            float p4 = Mathf.Clamp01(p4Pos);
            float p5 = Mathf.Clamp01(p5Pos);
            float p6 = Mathf.Clamp01(p6Pos);

            float r1 = Mathf.Max(0f, p1R);
            float r2 = Mathf.Max(0f, p2R);
            float r3 = p3R > 0f ? Mathf.Max(0f, p3R) : r2;
            float r4 = Mathf.Max(0f, p4R);
            float r5 = p5R > 0f ? Mathf.Max(0f, p5R) : r4;
            float r6 = p6R > 0f ? Mathf.Max(0f, p6R) : r5;

            // Ensure positions are non-decreasing
            if (p2 < 0f)  p2 = 0f;
            if (p3 < p2)  p3 = p2;
            if (p4 < p3)  p4 = p3;
            if (p5 < p4)  p5 = p4;
            if (p6 < p5)  p6 = p5;

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
            if (t <= p4)
            {
                float denom = p4 - p3;
                float seg = denom > 1e-5f ? (t - p3) / denom : 0f;
                return Mathf.Lerp(r3, r4, seg);
            }
            if (t <= p5)
            {
                float denom = p5 - p4;
                float seg = denom > 1e-5f ? (t - p4) / denom : 0f;
                return Mathf.Lerp(r4, r5, seg);
            }
            {
                float denom = p6 - p5;
                float seg = denom > 1e-5f ? (t - p5) / denom : 1f;
                return Mathf.Lerp(r5, r6, seg);
            }
        }

        /// <summary>
        /// 4-plane backward-compatible wrapper — delegates to RadiusAtT6 with planes 5/6
        /// collapsed onto plane 4 (degenerate segments, no change to profile).
        /// </summary>
        public static float RadiusAtT4(float t, float plane1R, float plane2R, float plane2Pos,
            float plane3R, float plane3Pos, float plane4R, float plane4Pos)
        {
            return RadiusAtT6(t,
                plane1R,
                plane2R, plane2Pos,
                plane3R, plane3Pos,
                plane4R, plane4Pos,
                plane4R, plane4Pos,
                plane4R, plane4Pos);
        }

        /// <summary>
        /// Compute radius at normalized position t (0=near, 1=far) using piecewise interpolation.
        /// Shared between CutMeshFrustum (for vertex testing) and PlaneVisualizer (for tube mesh).
        /// </summary>
        public static float RadiusAtT(float t, float nearR, float midR, float midPos, float farR)
        {
            return RadiusAtT4(t, nearR, midR, midPos, 0f, midPos, farR, 1f);
        }

        /// <summary>
        /// Backward-compatible cylinder cut (delegates to frustum with equal radii, infinite depth).
        /// </summary>
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
    }
}
