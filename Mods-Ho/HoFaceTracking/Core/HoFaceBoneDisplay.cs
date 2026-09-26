using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HoFaceTracking.Core
{
    /// <summary>
    /// Full hierarchy overlay, adapted from HoRuntimeBoneDebugRenderer without collection files.
    /// Owns only a temporary mesh/material: never creates objects inside the sampled hierarchy.
    /// Warudo uses the built-in pipeline; Camera.onPostRender draws over the character per view.
    /// </summary>
    public sealed class HoFaceBoneDisplay : IDisposable
    {
        static readonly List<HoFaceBoneDisplay> active = new List<HoFaceBoneDisplay>();
        static readonly Color X = new Color(1, .15f, .15f, 1);
        static readonly Color Y = new Color(.2f, 1, .2f, 1);
        static readonly Color Z = new Color(.2f, .45f, 1, 1);
        readonly List<Transform> nodes = new List<Transform>();
        readonly HashSet<Transform> nodeSet = new HashSet<Transform>();
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> indices = new List<int>();
        GameObject character;
        Mesh mesh;
        Material material;
        bool drawAxes;
        float axisLength, pixelWidth;
        Color boneColor;
        bool subscribed;
        int skippedNonFinite, skippedNearPlane, skippedDegenerate;
        bool warnedInvalidBuffers;

        public int NodeCount => nodes.Count;
        public int BoneCount { get; private set; }
        public int SegmentCount { get; private set; }
        public Mesh Geometry => mesh;

        /// <summary>本帧被跳过的线段数（非有限值 / 全在相机平面之后 / 退化）。</summary>
        public int SkippedSegments => skippedNonFinite + skippedNearPlane + skippedDegenerate;

        /// <summary>因缓冲不自洽而丢弃绘制的帧数（正常恒为 0；不为 0 说明出 bug 了）。</summary>
        public int DroppedFrames { get; private set; }

        /// <summary>最近一帧的几何摘要，排查"凭空多出乱三角形"时看这个。</summary>
        public string LastGeometrySummary { get; private set; } = "(未构建)";

        public void Update(GameObject target, bool axes, float length, float width, Color color)
        {
            if (target == null || !target.activeInHierarchy) { Dispose(); return; }
            character = target;
            drawAxes = axes;
            axisLength = Safe(length, .04f, 0, 1);
            pixelWidth = Safe(width, 2, .5f, 20);
            boneColor = new Color(Safe(color.r, .2f, 0, 1), Safe(color.g, .65f, 0, 1),
                Safe(color.b, 1, 0, 1), Safe(color.a, .9f, 0, 1));
            EnsureResources();
            CollectNodes();
            if (!subscribed)
            {
                active.Add(this);
                if (active.Count == 1) Camera.onPostRender += DrawAll;
                subscribed = true;
            }
        }

        void CollectNodes()
        {
            nodes.Clear(); nodeSet.Clear();
            // Deliberately includes inactive, helper and leaf transforms, just like the original
            // component with collection filtering disabled. No Humanoid/skin-bone restriction.
            character.GetComponentsInChildren(true, nodes);
            foreach (var node in nodes) if (node != null) nodeSet.Add(node);
            BoneCount = 0;
            foreach (var node in nodes)
                if (node != null && node.parent != null && nodeSet.Contains(node.parent)) BoneCount++;
        }

        void EnsureResources()
        {
            if (material == null)
            {
                // Unity's built-in vertex-color debug shader; no extra shader bundle or file input.
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) throw new InvalidOperationException("缺少 Unity 内置调试线条 Shader");
                material = new Material(shader) { name = "HoFace Bone Overlay", hideFlags = HideFlags.HideAndDontSave };
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_Cull", (int)CullMode.Off);
                material.SetInt("_ZWrite", 0);
                material.SetInt("_ZTest", (int)CompareFunction.Always);
                material.SetColor("_Color", Color.white);
                material.renderQueue = (int)RenderQueue.Overlay;
            }
            if (mesh == null)
            {
                mesh = new Mesh { name = "HoFace Bone Overlay Mesh", hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = IndexFormat.UInt32 };
                mesh.MarkDynamic();
            }
        }

        static void DrawAll(Camera camera)
        {
            if (camera == null || camera.cameraType != CameraType.Game) return;
            for (int i = active.Count - 1; i >= 0; i--) active[i].Draw(camera);
        }

        void Draw(Camera camera)
        {
            if (character == null || !character.activeInHierarchy || material == null || mesh == null) return;
            if ((camera.cullingMask & (1 << character.layer)) == 0) return;
            BuildGeometry(camera);
            if (mesh.vertexCount > 0 && material.SetPass(0))
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
        }

        /// <summary>Build against the rendering camera, never Camera.main (multi-view safe).</summary>
        public void BuildGeometry(Camera camera)
        {
            vertices.Clear(); colors.Clear(); indices.Clear(); SegmentCount = 0;
            skippedNonFinite = skippedNearPlane = skippedDegenerate = 0;
            if (mesh == null) return;
            mesh.Clear(false);
            if (camera == null || character == null || !character.activeInHierarchy) return;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                Vector3 origin = node.position;
                if (node.parent != null && nodeSet.Contains(node.parent))
                    AddSegment(camera, node.parent.position, origin, boneColor);
                if (!drawAxes || axisLength <= 0) continue;
                AddSegment(camera, origin, origin + node.right * axisLength, X);
                AddSegment(camera, origin, origin + node.up * axisLength, Y);
                AddSegment(camera, origin, origin + node.forward * axisLength, Z);
            }

            LastGeometrySummary = "verts=" + vertices.Count + " indices=" + indices.Count
                + " segments=" + SegmentCount + " skipped=" + skippedNonFinite + "/"
                + skippedNearPlane + "/" + skippedDegenerate;

            if (vertices.Count == 0) return;

            // 缓冲自检：不自洽就当帧不画。画出来比不画危害大得多 ——
            // 索引一旦指到别的线段上，顶点色会在红绿蓝之间插值，看起来正好是一个紫色大三角形。
            if (!ValidateBuffers())
            {
                mesh.Clear(false);
                DroppedFrames++;
                if (!warnedInvalidBuffers)
                {
                    warnedInvalidBuffers = true;
                    Debug.LogError("[Ho 面捕] 骨骼网格缓冲自检失败，已停止绘制：" + LastGeometrySummary);
                }
                return;
            }
            warnedInvalidBuffers = false;

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.RecalculateBounds();
            LastGeometrySummary += " bounds=" + mesh.bounds.size.ToString("F3");
        }

        /// <summary>顶点/颜色数量一致、索引数为 3 的倍数且全部在范围内。</summary>
        bool ValidateBuffers()
        {
            if (vertices.Count != colors.Count || indices.Count % 3 != 0) return false;
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index < 0 || index >= vertices.Count) return false;
            }
            return true;
        }

        void AddSegment(Camera camera, Vector3 start, Vector3 end, Color color)
        {
            if ((end - start).sqrMagnitude < 1e-12f) { skippedDegenerate++; return; }
            Vector3 a = camera.WorldToScreenPoint(start), b = camera.WorldToScreenPoint(end);
            if (!Finite(a) || !Finite(b)) { skippedNonFinite++; return; }
            float near = camera.nearClipPlane + .0001f;
            if (a.z < near && b.z < near) { skippedNearPlane++; return; }
            // Clip before projecting the quad. A segment crossing behind the camera must not
            // flip across the view or grow into a huge ribbon.
            if (a.z < near)
            {
                start = Vector3.Lerp(start, end, (near - a.z) / (b.z - a.z));
                a = camera.WorldToScreenPoint(start);
            }
            else if (b.z < near)
            {
                end = Vector3.Lerp(start, end, (near - a.z) / (b.z - a.z));
                b = camera.WorldToScreenPoint(end);
            }
            Vector2 direction = new Vector2(b.x - a.x, b.y - a.y);
            if (direction.sqrMagnitude < 1e-8f) { skippedDegenerate++; return; }
            Vector2 side = new Vector2(-direction.y, direction.x).normalized * (pixelWidth * .5f);
            int first = vertices.Count;
            AddVertex(camera.ScreenToWorldPoint(a + new Vector3(side.x, side.y, 0)), color);
            AddVertex(camera.ScreenToWorldPoint(a - new Vector3(side.x, side.y, 0)), color);
            AddVertex(camera.ScreenToWorldPoint(b + new Vector3(side.x, side.y, 0)), color);
            AddVertex(camera.ScreenToWorldPoint(b - new Vector3(side.x, side.y, 0)), color);
            indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
            indices.Add(first + 2); indices.Add(first + 1); indices.Add(first + 3);
            SegmentCount++;
        }

        void AddVertex(Vector3 point, Color color) { vertices.Add(point); colors.Add(color); }
        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x)
            && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        static float Safe(float v, float fallback, float min, float max) =>
            float.IsNaN(v) || float.IsInfinity(v) ? fallback : Mathf.Clamp(v, min, max);

        public void Dispose()
        {
            if (subscribed)
            {
                active.Remove(this);
                if (active.Count == 0) Camera.onPostRender -= DrawAll;
                subscribed = false;
            }
            DestroyOwned(mesh); DestroyOwned(material);
            mesh = null; material = null; character = null;
            nodes.Clear(); nodeSet.Clear(); vertices.Clear(); colors.Clear(); indices.Clear();
            BoneCount = SegmentCount = 0;
            skippedNonFinite = skippedNearPlane = skippedDegenerate = 0;
            warnedInvalidBuffers = false;
            LastGeometrySummary = "(未构建)";
        }

        static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
