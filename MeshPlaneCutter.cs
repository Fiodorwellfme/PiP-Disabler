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
        /// nearRadius = radius at startOffset (closest to camera)
        /// farRadius  = radius at startOffset + length (farthest from camera)
        ///   If nearRadius == farRadius → pure cylinder
        ///   If different → cone/frustum shape
        ///
        /// midRadius > 0 enables a two-segment profile: near→mid, then mid→far
        ///   midPosition = fractional position along the bore (0=near, 1=far)
        ///   This allows hourglass (mid &lt; near/far) or bulge (mid &gt; near/far) shapes
        ///
        /// startOffset = distance along axis from center to start of cut (toward camera = positive)
        /// length = depth of the cut volume
        ///
        /// keepInside=false → removes geometry inside the frustum (bore a hole)
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
            float localPreserve = nearPreserveDepth > 0f
                ? (avgScale > 0.001f ? nearPreserveDepth / avgScale : nearPreserveDepth)
                : 0f;

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
                    $"t=0.0→r={RadiusAtT(0f, localNearR, localMidR, localMidPos, localFarR):F5} " +
                    $"t=0.25→r={RadiusAtT(0.25f, localNearR, localMidR, localMidPos, localFarR):F5} " +
                    $"t=0.5→r={RadiusAtT(0.5f, localNearR, localMidR, localMidPos, localFarR):F5} " +
                    $"t=0.75→r={RadiusAtT(0.75f, localNearR, localMidR, localMidPos, localFarR):F5} " +
                    $"t=1.0→r={RadiusAtT(1f, localNearR, localMidR, localMidPos, localFarR):F5}");
            }

            // Capture values explicitly for the local function (avoid any closure ambiguity)
            float _cNearR = localNearR, _cFarR = localFarR, _cMidR = localMidR;
            float _cMidPos = localMidPos, _cStart = localStart, _cLen = localLen;
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
                float radiusAtDepth = RadiusAtT(t, _cNearR, _cMidR, _cMidPos, _cFarR);

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
        /// Multi-plane radial profile cut.
        ///
        /// offsets/radii define radial control points along axisWorld relative to centerWorld:
        ///   axial = offsets[i] => radius = radii[i]
        /// Radius between control points is linearly interpolated.
        ///
        /// keepInside=false removes geometry inside the radial profile volume.
        /// </summary>
        public static bool CutMeshRadialProfile(
            Mesh mesh,
            Transform meshTransform,
            Vector3 centerWorld,
            Vector3 axisWorld,
            float[] offsets,
            float[] radii,
            bool keepInside,
            float epsilon = 1e-5f)
        {
            if (mesh == null || offsets == null || radii == null) return false;
            if (offsets.Length < 2 || offsets.Length != radii.Length) return false;

            Vector3 cL = meshTransform.InverseTransformPoint(centerWorld);
            Vector3 aL = meshTransform.InverseTransformDirection(axisWorld).normalized;

            Vector3 lossyScale = meshTransform.lossyScale;
            float avgScale = (Mathf.Abs(lossyScale.x) + Mathf.Abs(lossyScale.y) + Mathf.Abs(lossyScale.z)) / 3f;

            int n = offsets.Length;
            var localOffsets = new float[n];
            var localRadii = new float[n];
            for (int i = 0; i < n; i++)
            {
                localOffsets[i] = avgScale > 0.001f ? offsets[i] / avgScale : offsets[i];
                localRadii[i] = avgScale > 0.001f ? radii[i] / avgScale : radii[i];
            }

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

            float RadiusAtOffset(float off)
            {
                if (off <= localOffsets[0]) return localRadii[0];
                if (off >= localOffsets[n - 1]) return localRadii[n - 1];

                for (int i = 0; i < n - 1; i++)
                {
                    float a = localOffsets[i];
                    float b = localOffsets[i + 1];
                    if (off >= a - epsilon && off <= b + epsilon)
                    {
                        float t = Mathf.Abs(b - a) > epsilon ? Mathf.Clamp01((off - a) / (b - a)) : 0f;
                        return Mathf.Lerp(localRadii[i], localRadii[i + 1], t);
                    }
                }

                return localRadii[n - 1];
            }

            bool IsInside(Vector3 v)
            {
                Vector3 diff = v - cL;
                float axialDist = Vector3.Dot(diff, aL);
                if (axialDist < localOffsets[0] - epsilon || axialDist > localOffsets[n - 1] + epsilon)
                    return false;

                float radiusAtDepth = RadiusAtOffset(axialDist);
                Vector3 projected = axialDist * aL;
                float perpDist = (diff - projected).magnitude;
                return perpDist <= radiusAtDepth + epsilon;
            }

            int AddVertexFromOld(int oldIndex)
            {
                if (keptMap.TryGetValue(oldIndex, out int ni)) return ni;
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
                    bool in0 = IsInside(verts[i0]);
                    bool in1 = IsInside(verts[i1]);
                    bool in2 = IsInside(verts[i2]);

                    bool keepTri = keepInside ? (in0 || in1 || in2) : (!in0 && !in1 && !in2);
                    if (!keepTri) continue;

                    outTris[s].Add(AddVertexFromOld(i0));
                    outTris[s].Add(AddVertexFromOld(i1));
                    outTris[s].Add(AddVertexFromOld(i2));
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
        /// Compute radius at normalized position t (0=near, 1=far) using piecewise interpolation.
        /// Shared between CutMeshFrustum (for vertex testing) and PlaneVisualizer (for tube mesh).
        /// </summary>
        public static float RadiusAtT(float t, float nearR, float midR, float midPos, float farR)
        {
            if (midR > 0f)
            {
                if (t <= midPos)
                {
                    float segT = midPos > 1e-5f ? t / midPos : 0f;
                    return Mathf.Lerp(nearR, midR, segT);
                }
                else
                {
                    float segT = (1f - midPos) > 1e-5f ? (t - midPos) / (1f - midPos) : 1f;
                    return Mathf.Lerp(midR, farR, segT);
                }
            }
            return Mathf.Lerp(nearR, farR, t);
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
