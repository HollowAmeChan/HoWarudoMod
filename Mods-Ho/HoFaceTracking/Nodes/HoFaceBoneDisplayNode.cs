using System;
using System.Collections.Generic;
using HoFaceTracking.Core;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;
using Warudo.Plugins.Core.Assets.Character;

namespace HoFaceTracking.Nodes
{
    [NodeType(Id = "8cc5509a-e3a4-4fca-990d-e8507ba0a146", Title = "HoFace骨骼全量显示", Category = "Ho Face Tracking")]
    public sealed class HoFaceBoneDisplayNode : Node
    {
        [DataInput(0), Label("角色")]
        public CharacterAsset Character;

        [DataInput(1), Label("显示")]
        public bool Show = true;

        [DataInput(2), Label("绘制轴向")]
        public bool DrawAxes = true;

        [DataInput(3), Label("轴向长度"), Description("世界单位；红 X、绿 Y、蓝 Z。")]
        public float AxisLength = .04f;

        [DataInput(4), Label("线宽（像素）")]
        public float LineWidth = 2;

        [DataInput(5), Label("骨链颜色")]
        public Color BoneColor = new Color(.2f, .65f, 1, .9f);

        static readonly List<HoFaceBoneDisplayNode> instances = new List<HoFaceBoneDisplayNode>();
        readonly HoFaceBoneDisplay display = new HoFaceBoneDisplay();
        string status = "等待角色";
        string lastError;

        protected override void OnCreate()
        {
            base.OnCreate();
            instances.Add(this);
        }

        // The plugin invokes this through Graph so connected inputs evaluate without output consumers.
        [FlowInput, Hidden]
        public Continuation RefreshDisplay()
        {
            if (!Show || Character == null || !Character.Active || Character.GameObject == null)
            {
                display.Dispose();
                status = !Show ? "已隐藏" : "等待可用角色";
                return null;
            }
            display.Update(Character.GameObject, DrawAxes, AxisLength, LineWidth, BoneColor);
            status = "节点 " + display.NodeCount + " 个 · 骨链 " + display.BoneCount
                + " 条（完整子层级，含辅助与末端节点；无集合过滤）";
            return null;
        }

        [DataOutput, Label("状态")]
        public string Status() => status;

        public static void UpdateAll()
        {
            for (int i = instances.Count - 1; i >= 0; i--)
            {
                var node = instances[i];
                if (node.Graph == null || !node.Graph.Enabled || node.Graph.IsBeingRemoved)
                {
                    node.display.Dispose(); node.status = "蓝图未启用";
                    continue;
                }
                try
                {
                    node.Graph.InvokeFlowAtInput(node, nameof(RefreshDisplay));
                    node.lastError = null;
                }
                catch (Exception e)
                {
                    node.display.Dispose(); node.status = "显示失败：" + e.Message;
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
            instances.Remove(this); display.Dispose();
            base.OnDestroy();
        }
    }
}
