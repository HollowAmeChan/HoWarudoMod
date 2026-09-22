# HoWarudoModTests

Warudo **各类 Mod 的最小可参考实现**。

放在 `BreakWarudo/Assets/HoWarudoModTests/` 下，是一个独立的 git 仓库，可以单独推到 GitHub。

**核心规则：一个目录一个类别。** 目录名就是 Warudo 数据目录里的目标目录名，
五个工作区（`Props` / `Particles` / `Environments` / `CharacterAnimations` / `Plugins`）
已经配在 `Assets/ExportSettings.asset` 里，一一对应。

做新 Mod 时照抄对应目录即可（每个目录里都有一份该类别的构建规范）。

> ## ⚠️ 文档标记约定
>
> 这些文档区分三类信息，请按标记采信：
>
> | 标记 | 含义 |
> |---|---|
> | ✅ | **本机实测**——有产物字节、日志或亲眼看到的 UI 作依据 |
> | 📖 | **官方文档所述**——来自 docs.warudo.app，**本机没有实测** |
> | ❓ | **未验证**——我的推断，**可能是错的** |
>
> 最典型的一条教训：角色动画的入口位置我原本写成"Source 下拉"，实际是**带搜索框的
> 卡片选择器**——那是 ❓ 被我当成了 ✅。所以 **Props / Particles / Environments 三类的
> UI 入口位置至今仍是 ❓**，需要实机确认。

---

## 五个类别

| 目录 | 类别 | 入口资产（名字必须一致） | 有脚本？ |
|---|---|---|---|
| `Mods/Props` | 道具 | `Prop.prefab` | ✅ 用来验证多文件脚本打包 |
| `Mods/Particles` | 粒子 | `Particle.prefab` | ❌ 纯资源 |
| `Mods/Environments` | 环境 | `Environment.unity`（挂 `EnvironmentSettings`） | ❌ 纯资源 |
| `Mods/CharacterAnimations` | 角色动画 | `Animation.anim` | ❌ 纯资源 |
| `Mods/Plugins` | 插件（蓝图节点） | 无入口资产，看 `[PluginType]` 类 | ✅ |

入口资产由 `1 - 生成各类别最小示例` 自动补齐（插件入口是源码，已经在仓库里）。

### 为什么不能把几个类别塞进一个目录

`.warudo` 包里**不存类型字段**（`modinfo.dat` 与 `sharedassets.meta` 都没有），
类型是"落点目录 + 入口资产名"两条约定。落点目录决定哪个内置 Asset 类型去枚举它：

- `Props/` → 蓝图里的 `道具源` 节点
- `Particles/` → 蓝图里的 `粒子效果源` 节点
- `Environments/` → 资源里的「环境」（环境组件 → 源）
- `CharacterAnimations/` → 资源里的「角色 → 动画 → 待机动画」
- `Plugins/` → 插件系统启动时加载，节点进蓝图节点新建列表

往同一个 `.warudo` 里塞两个类别的入口，**不会报错**，但只有落点目录对应的那一个会生效，
其余静默失效。所以：**一个类别一个目录，一个目录一个包。**

---

## 目录结构

```
HoWarudoModTests/
├── Editor/
│   └── HoModTestBuilder.cs           脚手架 + 工作区同步 + 构建 + 产物校验
├── Mods/
│   ├── Props/                        ← 道具（入口 Prop.prefab 自动生成）
│   │   ├── HoTestPropSpinner.cs      挂在 Prop 根节点上的 MonoBehaviour
│   │   ├── HoTestPropUtils.cs        同目录，但没有任何组件引用它
│   │   └── Internal/
│   │       └── HoTestPropInternal.cs 子目录里
│   ├── Particles/                    ← 粒子（入口 Particle.prefab 自动生成）
│   ├── Environments/                 ← 环境（入口 Environment.unity 自动生成）
│   ├── CharacterAnimations/          ← 角色动画（入口 Animation.anim 自动生成）
│   └── Plugins/                      ← 插件
│       ├── HoTestPlugin.cs           [PluginType] 入口
│       └── Nodes/
│           ├── HoTestGreetNode.cs    节点 1：四种端口齐全
│           └── Sub/
│               └── HoTestAddNode.cs  节点 2：嵌套子目录 + 纯数据节点
├── tools/
│   ├── compile-check.ps1             编译 Mod 脚本（对真机 Warudo DLL）
│   └── compile-check-editor.ps1      编译 Editor + Mods（对 UnityEditor + UMod SDK）
└── docs/
    └── 打包与脚本规范.md              规范与踩坑记录
```

`Props` 里那三个脚本是**故意**的：一个挂在 Prefab 上、一个没人引用、一个在子目录里 ——
用来证明 **UMod 会把工作区里所有 `.cs` 都编进 Mod 程序集**，与是否被引用无关。

`Mods/*` 里只有 `Props` 和 `Plugins` 有源码；另外三个是纯资源 Mod，产包里不会有
`assemblymodules.dat`，这也是一种合法形态。

---

## 怎么用

入口资产与工作区**都已经配好了**，这个仓库本身不再带任何编辑器脚本 —— 直接用 FastBuild 构建。

### 图形界面（真实路径）

窗口：`HoUnityTools → FastBuildWarudoMod` → 切到 **「其他 Mod」** 页。

对 `Mods/` 下的每一个类别目录各做一次：

1. 把 `Mods/<类别>/` 拖进 **「Mod 资产目录」**
2. 类型会自动识别（`Prop.prefab` → 道具、`[PluginType]` → 插件…）
3. 名称跟随目录名
4. 点 **「构建 Warudo Mod」**

五个工作区（`Props` / `Particles` / `Environments` / `CharacterAnimations` / `Plugins`）
已经写在 `Assets/ExportSettings.asset` 里，导出目录直接指向 Warudo 数据目录下的对应子目录，
所以在同一页的 **「Warudo 工作区」** 面板里也能直接切。

做新 Mod 的流程：复制一个类别目录 → 改名/改内容 → 在 FastBuild 里构建。

### 不启动 Unity 的编译自检（秒级）

```powershell
powershell -File tools\compile-check.ps1
```

对着**真机 Warudo DLL** 编译 `Mods/**/*.cs`。命名空间遮蔽、字段撞基类这类错误几秒就能发现，
不用等编辑器导入。换路径用 `-ManagedDir` / `-CscPath`。

编译产物写在 `.compile-check/` —— **Unity 会忽略任何以 `.` 开头的目录**，
所以产出的 DLL 不会被 Unity 当成插件导入。

### 怎么看 Warudo 认没认

Warudo 把加载过程写在 `%USERPROFILE%\AppData\LocalLow\HakuyaLabs\Warudo\Logs\*.log.gz`。

**只有 Plugins 与 Characters 会写 `Load mod: <名字>`**；Props / Particles / Environments /
CharacterAnimations 只会写一行 `Started monitoring <目录>`，**不记具体文件** ——
那几类必须进 Warudo 在 UI 里看：

| 包 | ✅ 已实机查验的入口 |
|---|---|
| `Plugins` | **蓝图 → 节点新建列表** → 分类 `HoWarudoModTests`（`Ho Test Greet` / `Ho Test Add`） |
| `Props` | **蓝图 → `道具源` 节点** → 道具来源里选 `Props` |
| `Particles` | **蓝图 → `粒子效果源` 节点** → 粒子来源里选 `Particles` |
| `Environments` | **资源 → 环境 → 环境组件 → 源** → 选 `Environments`（切过去能看到立方体标记物） |
| `CharacterAnimations` | **资源 → 角色 → 动画 → 待机动画** → 卡片网格里出现 `CharacterAnimations` |

> 以上**都不是唯一入口**，但都是可以直接验收的入口。其余入口未逐一查验。

### 实测结论：一个角色动画 Mod = 一个动画

角色动画选择器里**只有一张卡片，标的是 Mod 名**，没有第二级切片列表。
`Mods/CharacterAnimations/` 里放过的第二个切片（`Wave.anim`）**完全不会出现**。

所以：

* 入口切片**必须叫 `Animation`**，同目录里其它切片会被打包但不会被 Warudo 使用；
* 想要多个动画 = **建多个 Mod**，一个 Mod 一个 `Animation`；
* 卡片上的缩略图来自工作区的 **Mod Icon**（没设就是"无预览"）。


---

## 产物怎么判定

每个产物都是 `UMOD 头（12 字节）+ 标准 ZIP`。判据：

| 条目 | 纯资源 Mod | 带 C# 脚本的 Mod |
|---|---|---|
| `modinfo.dat` | ✅ | ✅ |
| `sharedassets.bin` / `.meta` | ✅ | ✅ |
| `sceneassets.bin` / `.meta` | 仅环境 Mod | — |
| **`assemblymodules.dat`** | ❌ | **✅ ← 有它才说明脚本真的被编译进去了** |

* `Props` / `Plugins` 应该有 `assemblymodules.dat`
* `Particles` / `CharacterAnimations` 不应该有（纯资源）
* `Environments` 应该用 `sceneassets.*` 而不是 `sharedassets.*`

如果带脚本的产包里**没有** `assemblymodules.dat`，说明 UMod 一个脚本都没编译，
99% 是工程根目录缺少 Unity 生成的 `.csproj`（见下）。

### 更进一步的类型级校验

`assemblymodules.dat` 里是编译后的托管程序集（`umod-compiled-<GUID>.dll`）。
要确认具体类型真的在里面，可以把内嵌 PE 抽出来看 ECMA-335 类型表 —— 工具在
HoUnityTools 仓库的 `.warudo-mod-research/.tools/` 下
（`extract_warudo_mod_assemblies.py` + `WarudoApiScan`）。

---

## 装进 Warudo 试

`2 - 同步工作区` 会把导出目录直接指到 Warudo 数据目录，所以构建完就已经装好了：

```
<Steam>\steamapps\common\Warudo\Warudo_Data\StreamingAssets\Props\Props.warudo
<Steam>\steamapps\common\Warudo\Warudo_Data\StreamingAssets\Plugins\Plugins.warudo
...
```

数据目录是**从你已有的工作区反推**的：取任意一个导出目录，末段是已知类别名就退一级。
推不出来时会报错并提示你先把某个工作区的导出目录指到 `StreamingAssets/<类别>`。

装好后：

* `Props` 里能选到 `Props` 这个道具，旋转动画会跑起来；
* 节点面板里出现 `HoWarudoModTests` 分类下的 `Ho Test Greet` 和 `Ho Test Add`；
* 放进去不用重启 Warudo —— 同名文件覆盖后它会自动重载。

---

## 已知坑

1. **必须有 Unity 生成的 `.csproj`。** UMod 是靠工程根目录的 `.csproj` 里
   `<Compile Include="Assets\...">` 决定编译哪些 `.cs` 的，不是直接扫描 Mod 目录。
   工程根目录没有 `Assembly-CSharp.csproj` 时，构建会"成功"但产物里没有
   `assemblymodules.dat`，组件在 Warudo 里全是 Missing Script。
   修法：`Edit > Preferences > External Tools` 选好代码编辑器 → `Regenerate project files`。
   `HoModTestBuilder` 在构建前会检查并按需触发重新生成。

2. **一个目录只能放一个类别。** 见上文。混放不报错，只是静默失效。

3. **Warudo 是 BiRP，不是 URP。** 道具/粒子的材质请用 Built-in 的 `Standard` 等着色器。
   用 `Universal Render Pipeline/Lit` 做出来的 Mod 在 Warudo 里会丢材质。

4. **Mod 脚本不能用 `System.Reflection`，也不能用 `System.IO`。**
   UMod 在编译后会做 API 引用审查，命中直接构建失败。
   连 `exception.GetType().Name` 这种写法也会被拒（IL 层是 `MemberInfo.Name`）。

5. **插件 Mod 导出前先关掉 Warudo**，并确认 `StreamingAssets/Playground` 下没有同名脚本，
   否则会冲突。

6. **不要给 Mod 工作区加 `.asmdef`。** 被 asmdef 覆盖的脚本不会被打包进 Mod。

7. **`.meta` 要提交。** 这个仓库是 Unity 工程的一部分，Unity 会为每个文件生成 `.meta`；
   首次在 Unity 里导入后请把生成的 `.meta` 一起提交，否则别人拉下来引用会错位。

8. **命名空间不要起成 `...Plugin`。** `using Warudo.Core.Plugins` 里的 `Plugin` 基类会被
   自己所在的命名空间遮蔽，报
   `CS0118: "Plugin" 是命名空间，但此处被当做类型来使用`。
   本仓库用的是 `HoWarudoModTests.Plugins`，就是为了避开这个坑。

9. **`[DataInput]` 字段名不要撞 `Node` 基类成员。** 例如 `Name` 会报
   `CS0108: HoTestGreetNode.Name 隐藏继承的成员 Node.Name`，
   数据输入请取业务名（本仓库用 `Who`），否则会遮蔽基类字段。

10. **`.ps1` 要用纯 ASCII 或带 BOM。** Windows PowerShell 5.1 把无 BOM 的文件按 ANSI 读，
    中文注释会直接把脚本读崩。`tools/*.ps1` 因此写成全英文。
