// HoTestCharacterDaemonAsset.cs  -- CharacterDaemonAsset 基类
//
// CharacterDaemonAsset 是「角色骨骼驱动」类资源的基类，追踪器
// （GenericTrackerAsset）就是继承它的。
//
// ✅ 探针实测：它**没有任何抽象成员**。也就是说派生一个能用的实例不需要实现任何东西 ——
//    这是全部 7 个可派生资源基类里最省事的一个。
//    它自带两个 [DataInput]：CharacterAsset Character、bool ShowCharacterDaemon。
//
// 因为没有抽象成员，这个文件的价值不在「教你怎么实现」，而在于：
//   1) 证明这一类资源确实可以不写任何逻辑就注册出来
//   2) 演示「资源自己起一个 MonoBehaviour 干脏活」的标准姿势
//      （CharacterDaemonBehavior 就是这么干的，本文件照抄这个形态）

using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Plugins.Core.Assets.Character;

namespace HoWarudoModTests.CustomAsset
{
    [AssetType(
        Id = "f5b81c26-7ed9-40a4-c25b-9d6e3a7b1f85",
        Title = "Ho Test Character Daemon",
        Category = "CATEGORY_DEBUG")]
    public class HoTestCharacterDaemonAsset : CharacterDaemonAsset
    {
        [Markdown]
        public string Status = "idle";

        // 每隔几秒往日志里打一条，用来确认它真的在跑。
        [DataInput]
        public float PingInterval = 5f;

        private float _elapsed;
        private int _pings;

        protected override void OnCreate()
        {
            base.OnCreate();
            SetActive(true);
            Status = "created";
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (PingInterval <= 0f)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed < PingInterval)
            {
                return;
            }
            _elapsed = 0f;

            _pings++;
            Status = "alive, pings=" + _pings
                     + ", character=" + (Character != null ? Character.Name : "<none>");
            BroadcastDataInput(nameof(Status));

            // 纯逻辑资源想让外面看见，最省事的办法就是打日志。
            Debug.Log("[HoTestCharacterDaemon] " + Status);
        }
    }
}
