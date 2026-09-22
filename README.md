# HoWarudoModTests

Warudo **非角色类 Mod** 的最小打包测试工程。

放在 `BreakWarudo/Assets/HoWarudoModTests/` 下，是一个独立的 git 仓库，可以单独推到 GitHub。

它验证两件事：

1. **道具类 Mod（资源型）** 怎么打包 —— `Mods/HoTestProp`
2. **插件类 Mod（蓝图节点型）** 怎么打包 —— `Mods/HoTestPlugin`
3. 顺带证明**多文件 / 多级子目录的脚本**会被完整编进 Mod 程序集

---

## 目录结构

```
HoWarudoModTests/
├── Editor/
│   └── HoModTestBuilder.cs          构建入口（菜单 + batchmode）
├── Mods/
│   ├── HoTestProp/                  ← 道具 Mod 工作区（modAssetPath）
│   │   ├── HoTestPropSpinner.cs     脚本 1：挂在 Prop 根节点上的 MonoBehaviour
│   │   ├── HoTestPropUtils.cs       脚本 2：同目录，但没有任何组件引用它
│   │   └── Internal/
│   │       └── HoTestPropInternal.cs 脚本 3：子目录里
│   └── HoTestPlugin/                ← 插件 Mod 工作区
│       ├── HoTestPlugin.cs          [PluginType] 入口
│       └── Nodes/
│           ├── HoTestGreetNode.cs   节点 1：四种端口齐全
│           └── Sub/
│               └── HoTestAddNode.cs 节点 2：嵌套子目录 + 纯数据节点
└── docs/
    └── 打包与脚本规范.md             规范与踩坑记录
```

`Prop.prefab` 由构建脚本生成，不在仓库里（`out/` 也被 gitignore）。

---

## 怎么用

### 图形界面

Unity 菜单栏依次执行：

| 菜单 | 作用 |
|---|---|
| `HoWarudoModTests/1 - 生成 Prop 预制体` | 在 `Mods/HoTestProp/` 下生成根名为 `Prop` 的预制体，并挂上 `HoTestPropSpinner` |
| `HoWarudoModTests/2 - 配置导出工作区` | 建两个 Export Profile：`HoTestProp` → `out/Props`，`HoTestPlugin` → `out/Plugins` |
| `HoWarudoModTests/3 - 构建全部 Mod` | 调用官方构建入口，产出两个 `.warudo` |
| `HoWarudoModTests/4 - 校验产物 .warudo` | 自己解析产物，打印包内条目与关键判定 |

### 命令行（batchmode）

```powershell
& "D:\Unity\Unity 2021.3.45f2\Editor\Unity.exe" -batchmode `
  -projectPath "D:\Unity_Project\BreakWarudo" `
  -executeMethod HoWarudoModTests.EditorTools.HoModTestBuilder.RunAll `
  -logFile "D:\Unity_Project\BreakWarudo\Assets\HoWarudoModTests\out\build.log"
```

`RunAll` = 1 → 2 → 3 → 4，最后把报告写到 `out/build-report.txt`，退出码 0 表示全部成功。

> 注意：`Unity.exe` 是 GUI 程序，PowerShell 里用 `&` 调用**不会等待**。要等它结束请用
> `Start-Process -Wait -PassThru`。

---

## 产物怎么判定

构建完成后，`out/Props/HoTestProp.warudo` 和 `out/Plugins/HoTestPlugin.warudo` 是两个
`UMOD 头（12 字节）+ 标准 ZIP` 的包。判据：

| 条目 | 纯资源 Mod | 带 C# 脚本的 Mod |
|---|---|---|
| `modinfo.dat` | ✅ | ✅ |
| `sharedassets.bin` / `.meta` | ✅ | ✅ |
| `sceneassets.bin` / `.meta` | 仅环境 Mod | — |
| **`assemblymodules.dat`** | ❌ | **✅ ← 有它才说明脚本真的被编译进去了** |

* `HoTestProp` 挂了自定义 MonoBehaviour → 应该有 `assemblymodules.dat`
* `HoTestPlugin` 是插件 → 必须有 `assemblymodules.dat`

如果 `HoTestPlugin.warudo` 里**没有** `assemblymodules.dat`，说明 UMod 一个脚本都没编译，
99% 是工程根目录缺少 Unity 生成的 `.csproj`（见下）。

### 更进一步的类型级校验

`assemblymodules.dat` 里是编译后的托管程序集（`umod-compiled-<GUID>.dll`）。
要确认 `HoTestPropSpinner` / `HoTestPlugin` / 两个 Node 类型真的在里面，
可以把内嵌 PE 抽出来看 ECMA-335 类型表 —— 工具在 HoUnityTools 仓库的
`.warudo-mod-research/.tools/` 下（`extract_warudo_mod_assemblies.py` + `WarudoApiScan`）。

---

## 装进 Warudo 试

把产物放到 Warudo 数据目录：

```
<Steam>\steamapps\common\Warudo\Warudo_Data\StreamingAssets\Props\HoTestProp.warudo
<Steam>\steamapps\common\Warudo\Warudo_Data\StreamingAssets\Plugins\HoTestPlugin.warudo
```

也可以在 `2 - 配置导出工作区` 之后手动把 `OutputRoot` 改成数据目录，直接构建到目标位置。

装好后：

* `Props` 里应能选到 `HoTestProp` 这个道具，旋转动画会跑起来；
* 节点面板里应出现 `HoWarudoModTests` 分类下的 `Ho Test Greet` 和 `Ho Test Add`。

---

## 已知坑

1. **必须有 Unity 生成的 `.csproj`。** UMod 是靠工程根目录的 `.csproj` 里
   `<Compile Include="Assets\...">` 决定编译哪些 `.cs` 的，不是直接扫描 Mod 目录。
   工程根目录没有 `Assembly-CSharp.csproj` 时，构建会"成功"但产物里没有
   `assemblymodules.dat`，组件在 Warudo 里全是 Missing Script。
   修法：`Edit > Preferences > External Tools` 选好代码编辑器 → `Regenerate project files`。
   `HoModTestBuilder` 在构建前会检查并按需触发重新生成。

2. **Warudo 是 BiRP，不是 URP。** 道具/粒子的材质请用 Built-in 的 `Standard` 等着色器。
   用 `Universal Render Pipeline/Lit` 做出来的 Mod 在 Warudo 里会丢材质。

3. **Mod 脚本不能用 `System.Reflection`，也不能用 `System.IO`。**
   UMod 在编译后会做 API 引用审查，命中直接构建失败。
   连 `exception.GetType().Name` 这种写法也会被拒（IL 层是 `MemberInfo.Name`）。

4. **插件 Mod 导出前先关掉 Warudo**，并确认 `StreamingAssets/Playground` 下没有同名脚本，
   否则会冲突。

5. **不要给 Mod 工作区加 `.asmdef`。** 被 asmdef 覆盖的脚本不会被打包进 Mod。

6. **`.meta` 要提交。** 这个仓库是 Unity 工程的一部分，Unity 会为每个文件生成 `.meta`；
   首次在 Unity 里导入后请把生成的 `.meta` 一起提交，否则别人拉下来引用会错位。
