// HoTestPropUtils.cs  -- 道具类 Mod 脚本 2/3（同目录，但没有任何组件引用它）
//
// 存在的意义：证明 Mod 工作区里的每一个 .cs 都会被 UMod 编译进 Mod 程序集，
// 而不是"只有挂在 Prefab 上的脚本才被打包"。

namespace HoWarudoModTests.Props
{
    public static class HoTestPropUtils
    {
        public const string ModTag = "ho-test-prop-utils";

        public static string Describe()
        {
            return "HoTestProp helper (not referenced by any component)";
        }
    }
}
