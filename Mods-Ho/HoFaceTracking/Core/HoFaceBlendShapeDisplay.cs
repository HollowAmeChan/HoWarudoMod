using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace HoFaceTracking.Core
{
    /// <summary>
    /// Node-owned world text, adapted from HoWarudoBlendShapeBillboard.
    /// Reads real renderer weights. Same-name keys merge by MAX (not sum or absolute value);
    /// differing weights make the name yellow. Never writes to the character.
    /// </summary>
    public sealed class HoFaceBlendShapeDisplay : IDisposable
    {
        public sealed class Entry
        {
            public string Name;
            public float Value;
            public bool Conflict;
            internal float First;
            internal bool Sampled;
        }

        sealed class Source
        {
            public SkinnedMeshRenderer Renderer;
            public Mesh Mesh;
            public int[] Keys;
        }

        sealed class Column
        {
            public TextMesh Names;
            public TextMesh Conflicts;
            public TextMesh Values;
            public string LastNames, LastConflicts, LastValues;
            public float Width;
        }

        readonly List<SkinnedMeshRenderer> candidates = new List<SkinnedMeshRenderer>();
        readonly List<Source> sources = new List<Source>();
        readonly List<Entry> entries = new List<Entry>();
        readonly Dictionary<string, int> indices = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<Column> columns = new List<Column>();
        readonly StringBuilder names = new StringBuilder();
        readonly StringBuilder conflicts = new StringBuilder();
        readonly StringBuilder values = new StringBuilder();
        GameObject owner;
        GameObject root;
        Font font;
        int lastRows;
        float lastRowSpacing;
        float lastColumnSpacing;
        bool layoutDirty;

        public IReadOnlyList<Entry> Entries => entries;
        public int ConflictCount { get; private set; }
        public int RendererCount => sources.Count;
        public GameObject DisplayObject => root;

        public void Update(GameObject character, int rows, float rowSpacing, float columnSpacing,
            Vector3 offset, float scale, Vector3 extraEuler = default(Vector3), bool reverseColumns = false)
        {
            if (character == null || !character.activeInHierarchy) { Dispose(); return; }
            if (owner != character) { Dispose(); owner = character; }
            rows = Mathf.Clamp(rows, 1, 200);
            rowSpacing = Safe(rowSpacing, 1, .1f, 5);
            columnSpacing = Safe(columnSpacing, 1.5f, 0, 20);
            scale = Safe(scale, .035f, .001f, 1);
            offset = new Vector3(Safe(offset.x, .6f, -100, 100), Safe(offset.y, 1.8f, -100, 100),
                Safe(offset.z, 0, -100, 100));

            CollectSources(character);
            Sample();
            EnsureRoot();
            int needed = Mathf.Max(1, (entries.Count + rows - 1) / rows);
            while (columns.Count < needed) columns.Add(CreateColumn());
            if (lastRows != rows || lastRowSpacing != rowSpacing || lastColumnSpacing != columnSpacing)
                layoutDirty = true;
            lastRows = rows; lastRowSpacing = rowSpacing; lastColumnSpacing = columnSpacing;

            float x = 0;
            for (int c = 0; c < columns.Count; c++)
            {
                var column = columns[c];
                bool active = c < needed;
                column.Names.gameObject.SetActive(active);
                column.Conflicts.gameObject.SetActive(active);
                column.Values.gameObject.SetActive(active);
                if (!active) continue;
                names.Length = conflicts.Length = values.Length = 0;
                for (int i = c * rows; i < Mathf.Min((c + 1) * rows, entries.Count); i++)
                {
                    if (i > c * rows) { names.Append('\n'); conflicts.Append('\n'); values.Append('\n'); }
                    var entry = entries[i];
                    (entry.Conflict ? conflicts : names).Append(entry.Name);
                    values.Append(entry.Value.ToString("F2", CultureInfo.InvariantCulture));
                }
                if (entries.Count == 0) names.Append("没有形态键");
                string nameText = names.ToString(), conflictText = conflicts.ToString(), valueText = values.ToString();
                bool changed = nameText != column.LastNames || conflictText != column.LastConflicts
                    || valueText != column.LastValues;
                if (changed)
                {
                    column.Names.text = column.LastNames = nameText;
                    column.Conflicts.text = column.LastConflicts = conflictText;
                    column.Values.text = column.LastValues = valueText;
                }
                if (layoutDirty || changed)
                {
                    float nameWidth = Mathf.Max(MeasureWidth(column.Names, nameText), MeasureWidth(column.Conflicts, conflictText));
                    // Reserve numeric width so other columns don't jump when 0 becomes 100.
                    float valueWidth = Mathf.Max(MeasureWidth(column.Values, "-1000.00"), MeasureWidth(column.Values, valueText));
                    column.Values.transform.localPosition = new Vector3(nameWidth + .75f, 0, 0);
                    column.Width = nameWidth + .75f + valueWidth + columnSpacing;
                }
                Style(column.Names, rowSpacing, character.layer);
                Style(column.Conflicts, rowSpacing, character.layer);
                Style(column.Values, rowSpacing, character.layer);
                // 列铺开方向。默认沿本地 +X；reverseColumns 时翻到 -X。
                // 注意：负责文字正反面的那个 180° 旋转会**同时**把 +X 翻成观察者的左边，
                // 所以「面板要长在哪一侧」只能用这个开关调，用旋转调会把文字也翻掉。
                column.Names.transform.parent.localPosition = new Vector3(reverseColumns ? -x : x, 0, 0);
                x += column.Width;
            }
            layoutDirty = false;
            root.layer = character.layer;
            root.transform.position = character.transform.TransformPoint(offset);
            root.transform.localScale = Vector3.one * scale;
            var camera = Camera.main;
            Vector3 direction = camera != null ? camera.transform.position - root.transform.position : -character.transform.forward;
            if (direction.sqrMagnitude > .000001f)
                root.transform.rotation = Quaternion.LookRotation(direction, camera != null ? camera.transform.up : Vector3.up)
                    * Quaternion.Euler(0, 180, 0)
                    * Quaternion.Euler(extraEuler);
        }

        void CollectSources(GameObject character)
        {
            candidates.Clear();
            character.GetComponentsInChildren(true, candidates);
            int count = 0;
            bool changed = false;
            foreach (var renderer in candidates)
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0) continue;
                if (count >= sources.Count || sources[count].Renderer != renderer || sources[count].Mesh != mesh
                    || sources[count].Keys.Length != mesh.blendShapeCount) changed = true;
                count++;
            }
            if (!changed && count == sources.Count) return;
            sources.Clear(); entries.Clear(); indices.Clear();
            foreach (var renderer in candidates)
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0) continue;
                var source = new Source { Renderer = renderer, Mesh = mesh, Keys = new int[mesh.blendShapeCount] };
                for (int i = 0; i < source.Keys.Length; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    if (!indices.TryGetValue(name, out int index))
                    {
                        index = entries.Count;
                        indices.Add(name, index);
                        entries.Add(new Entry { Name = name });
                    }
                    source.Keys[i] = index;
                }
                sources.Add(source);
            }
            layoutDirty = true;
        }

        void Sample()
        {
            foreach (var entry in entries) { entry.Sampled = false; entry.Conflict = false; }
            foreach (var source in sources)
            {
                for (int i = 0; i < source.Keys.Length; i++)
                {
                    var entry = entries[source.Keys[i]];
                    float value = source.Renderer.GetBlendShapeWeight(i);
                    if (!entry.Sampled) { entry.Value = entry.First = value; entry.Sampled = true; }
                    else
                    {
                        if (!value.Equals(entry.First)) entry.Conflict = true;
                        entry.Value = Mathf.Max(entry.Value, value);
                    }
                }
            }
            ConflictCount = 0;
            foreach (var entry in entries) if (entry.Conflict) ConflictCount++;
        }

        void EnsureRoot()
        {
            if (root != null) return;
            root = new GameObject("HoFace形态键真值");
            root.hideFlags = HideFlags.HideAndDontSave;
            // Unity 6 renamed Arial; Warudo's Unity 2021 player still uses Arial.ttf.
            font = Resources.GetBuiltinResource<Font>(Application.unityVersion.StartsWith("6000.", StringComparison.Ordinal)
                ? "LegacyRuntime.ttf" : "Arial.ttf");
            layoutDirty = true;
        }

        Column CreateColumn()
        {
            var parent = new GameObject("Column" + columns.Count);
            parent.hideFlags = HideFlags.HideAndDontSave;
            parent.transform.SetParent(root.transform, false);
            return new Column { Names = CreateText(parent.transform, "Names", Color.white),
                Conflicts = CreateText(parent.transform, "Conflicts", Color.yellow),
                Values = CreateText(parent.transform, "Values", Color.white) };
        }

        TextMesh CreateText(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMesh>();
            text.richText = false; // Shape names are literal; no rich-text injection or escaping.
            text.font = font;
            text.fontSize = 48;
            // TextMesh glyph advances are in font pixels * characterSize / 10.
            // Keep one em = one local unit while using a high-resolution font atlas.
            text.characterSize = 10f / text.fontSize;
            text.anchor = TextAnchor.UpperLeft;
            text.alignment = TextAlignment.Left;
            text.color = color;
            var renderer = text.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return text;
        }

        static void Style(TextMesh text, float rowSpacing, int layer)
        {
            text.lineSpacing = rowSpacing;
            text.gameObject.layer = layer;
        }

        // TextMesh uses /10, NOT /fontSize (the old component underestimated the column widths).
        static float MeasureWidth(TextMesh text, string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            text.font.RequestCharactersInTexture(value, text.fontSize, text.fontStyle);
            float line = 0, widest = 0;
            foreach (char c in value)
            {
                if (c == '\n') { widest = Mathf.Max(widest, line); line = 0; continue; }
                if (text.font.GetCharacterInfo(c, out CharacterInfo info, text.fontSize, text.fontStyle))
                    line += info.advance * text.characterSize * .1f;
                else line += text.fontSize * text.characterSize * .05f;
            }
            return Mathf.Max(widest, line);
        }

        static float Safe(float value, float fallback, float min, float max) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        public void Dispose()
        {
            if (root != null)
            {
                root.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
            root = null; owner = null; font = null;
            columns.Clear(); sources.Clear(); candidates.Clear(); entries.Clear(); indices.Clear();
            ConflictCount = 0;
        }
    }
}
