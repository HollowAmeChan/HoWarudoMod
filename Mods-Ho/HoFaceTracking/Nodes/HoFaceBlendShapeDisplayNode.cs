using System;
using System.Collections.Generic;
using HoFaceTracking.Core;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;
using Warudo.Plugins.Core.Assets.Character;

namespace HoFaceTracking.Nodes
{
    [NodeType(Id = "f4b5af55-3161-4a65-b6e0-f9d8975d1f12", Title = "HoFace形态键真值", Category = "Ho Face Tracking")]
    public sealed class HoFaceBlendShapeDisplayNode : Node
    {
        [DataInput(0), Label("角色")]
        public CharacterAsset Character;

        [DataInput(1), Label("显示")]
        public bool Show = true;

        [DataInput(2), Label("每列行数")]
        public int RowsPerColumn = 24;

        [DataInput(3), Label("行距")]
        public float RowSpacing = 1;

        [DataInput(4), Label("列距")]
        public float ColumnSpacing = 1.5f;

        [DataInput(5), Label("位置偏移"), Description("相对角色根的位置；文字自动面向主相机。")]
        public Vector3 Offset = new Vector3(.6f, 1.8f, 0);

        [DataInput(6), Label("整体缩放")]
        public float Scale = .035f;

        [DataInput(7), Label("附加旋转")]
        public Vector3 ExtraRotation = Vector3.zero;

        [DataInput(8), Label("列反向展开")]
        public bool ReverseColumns = true;

        static readonly List<HoFaceBlendShapeDisplayNode> instances = new List<HoFaceBlendShapeDisplayNode>();
        readonly HoFaceBlendShapeDisplay display = new HoFaceBlendShapeDisplay();
        string status = "等待角色";
        string lastError;

        protected override void OnCreate()
        {
            base.OnCreate();
            instances.Add(this);
        }

        // An internal flow entry lets Warudo evaluate the entire upstream dependency chain,
        // including wired Show/layout inputs, even when none of this node's outputs are connected.
        [FlowInput, Hidden]
        public Continuation RefreshDisplay()
        {
            if (!Show || Character == null || !Character.Active || Character.GameObject == null)
            {
                display.Dispose();
                status = !Show ? "已隐藏" : "等待可用角色";
                return null;
            }
            display.Update(Character.GameObject, RowsPerColumn, RowSpacing, ColumnSpacing, Offset, Scale,
                ExtraRotation, ReverseColumns);
            status = "形态键 " + display.Entries.Count + " 个 · 同名值不同 " + display.ConflictCount
                + " 个（黄字，取最大值；原始权重，不除以 100）";
            return null;
        }

        [DataOutput, Label("状态")]
        public string Status() => status;

        /// <summary>Plugin drives this after scene LateUpdate, including cleanup of disabled graphs.</summary>
        public static void UpdateAll()
        {
            for (int i = instances.Count - 1; i >= 0; i--)
            {
                var node = instances[i];
                if (node.Graph == null || !node.Graph.Enabled || node.Graph.IsBeingRemoved)
                {
                    node.display.Dispose();
                    node.status = "蓝图未启用";
                    continue;
                }
                try
                {
                    node.Graph.InvokeFlowAtInput(node, nameof(RefreshDisplay));
                    node.lastError = null;
                }
                catch (Exception e)
                {
                    node.display.Dispose();
                    node.status = "显示失败：" + e.Message;
                    if (node.lastError != e.Message) Debug.LogWarning("[Ho 面捕] " + node.status);
                    node.lastError = e.Message;
                }
            }
        }

        public static void ClearAll()
        {
            foreach (var node in instances) node.display.Dispose();
            instances.Clear();
        }

        protected override void OnDestroy()
        {
            instances.Remove(this);
            display.Dispose();
            base.OnDestroy();
        }
    }
}
