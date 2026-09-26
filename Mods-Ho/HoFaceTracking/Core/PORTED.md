# 面捕规则源码同步

主本在 `D:/Unity_Fork/HoUnityTools/Runtime/FaceTracking/`。Mod 保留独立源码副本；
不使用跨工程引用、junction 或构建时临时补源码。

## 公共同源的 10 份文件

| 文件 | 职责 |
|---|---|
| `HoFaceExpression.cs` | 表达式解析、变量与 `out("参数名")` 两个读取入口 |
| `HoFaceMiddleware.cs` | 行数据、曲线、修饰符定义、输出引用顺序校验 |
| `HoFaceOutputTable.cs` | 本帧输出表：只读上方最近写入值，同名后写覆盖 |
| `HoFaceProfile.cs` | JSON 格式与入口 |
| `HoFaceProfileJson.cs` | 配置读取与写入 |
| `HoJson.cs` | 公用 JSON 读取器 |
| `HoVtsPacket.cs` | VTS iPhone 报文 |
| `HoFaceTrackingChannels.cs` | 规范通道名与别名 |
| `HoFaceNaming.cs` | 参数命名约定 |
| `HoFaceSemanticHub.cs` | 角色动态参数 Hub |

包仓库中可追踪的同步工具是 `Tests~/SyncFaceModCore.ps1`，旧
`.research/sync-modcore.ps1` 在本机只转调这个工具。

```powershell
& D:/Unity_Fork/HoUnityTools/Tests~/SyncFaceModCore.ps1
& D:/Unity_Fork/HoUnityTools/Tests~/SyncFaceModCore.ps1 -Check
```

只改包侧主本，再同步；代码只替换命名空间 `Hollow.HoUnityTools.FaceTracking` →
`HoFaceTracking.Core`，加来源文件头，归一 UTF-8 无 BOM / LF。`-Check` 不写文件，
缺失或漂移即失败。已有 `.meta` 不变，新文件的 `.meta` 与源码一起保存。
同步不触碰清单外的源码，也不自动提交。

## Mod 独立实现

`HoFaceChain.cs` 是参数求值执行器，与 Unity 的 `HoFaceAnimationSession` 对齐：

1. 输入行读取原始字典；缺键保持上一帧，初值来自 `defaultValue`。
2. 输出按文件行序求值；`out()` 读上方最近一条同名行经过曲线、修饰符、极小值归零后的值。
3. 同名行可反复读写；每行有独立修饰符状态，最终只发布最后写入的值。
4. 前向／缺失引用和输入行误用 `out()` 报诊断，使用该行默认值；空表达式是正常常量行。
5. 平滑、延迟、维持按列表顺序运行。首帧与重建配置后的首帧初始化状态，不从零爬升。
6. 每帧清空输出表。`ARKit/` 前缀与别名转换仅在最终参数发布时发生，`out()` 使用配置原名。

Unity 的预览覆盖、通道模式、附加通道曲线与断流回中性属于调试环境，并非 JSON 规则。
规则一致性比较时，两端应收到相同的原始值与时间步；调试通道桥接不额外改值。

其他 Mod 独立文件仍包括 `HoFaceSolver.cs`、`HoFaceController.cs`、`HoFaceInputState.cs`、
`HoFaceProfileStore.cs`、`HoVtsIphoneReceiver.cs`、`HoVtsApiServer.cs`、`HoVtsApiPacket.cs`。
这些文件不由同步工具覆盖。沙箱文件读取继续使用 Warudo 的持久化 API，不引入 `System.IO`。

## 回归

包仓库 `Tests~/FaceRuleParity/Run.ps1` 将这边实际源码复制到独立测试工程，
与 Unity 编辑器实际规则方法在真实 Unity 曲线引擎下逐帧比较。详见同目录 README。
修改公共文件或者任一端执行器，都应同时跑这个测试。

2026-09-27：以用户的 `BREAK_URP/Assets/Hollow/土豆/FT/ho-iPhoneVTS.hoface.json`
验证（71 输入、136 输出、134 个最终参数），连同边界用例共 2,010 帧、417,343 次比较通过。
Mod 真实 Warudo DLL 编译与 UMod lint 通过；Unity 完整播放回归通过。
这是源码和测试验证，不表示已经将新 Mod 打包、加载进 Warudo 播放器。

**所有数据路径继续禁止使用 `JsonUtility`**：播放器会丢嵌套列表；JSON 读取器必须随同同步。
Hub 序列化字段保持 public，不在同步源码中加两端行为不同的条件编译。
