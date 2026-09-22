// HoTestPropSpinner.cs  -- 道具类 Mod 脚本 1/3（位于 Mod 工作区根目录）
//
// 道具 Mod 的本质：一个根 Prefab 名为 "Prop" 的 Unity 预制体。
// 挂在它上面的自定义 MonoBehaviour 必须位于 Mod 工作区内，
// 否则 UMod 导出时找不到源码，Warudo 里会变成 Missing Script。

using UnityEngine;

namespace HoWarudoModTests.Props
{
    /// <summary>Minimal behaviour: spins the prop around its local Y axis.</summary>
    public class HoTestPropSpinner : MonoBehaviour
    {
        public float DegreesPerSecond = 90f;

        private void Update()
        {
            transform.Rotate(0f, DegreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }
}
