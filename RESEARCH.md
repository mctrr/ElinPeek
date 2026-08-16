# 窥视（Peek）Mod —— 研究与实现

> 受 CDDA（Cataclysm: Dark Days Ahead）启发：PC 可以在拐角处“探出头”观察敌人，
> 而不会被敌人发现。本文档记录研究结论、实现方案与待验证项。

## 1. 结论摘要

**可行性：高。** 只需 4 个 Harmony 补丁点即可实现，不涉及存档/数据表改动：

| 需求 | 实现方式 |
|---|---|
| 拐角后能看到敌人 | 把 PC 视野（Fov）原点临时重定向到拐角格子 |
| 不被敌人发现 | 窥视期间冻结世界（gameSpeed=0）；敌人 AI 只看“自己能否看到 PC”，墙挡视线 |
| 按钮 | 在 PC 自身动作计划（ActPlan）里注入一个自定义 Act「窥视」 |
| 方向选择 | 鼠标悬停即窥视：视野跟随鼠标实时换方向，右键/Esc/左键退出 |
| 多语言 | CN/EN/JP 代码内词典 + `T(key)` 解析（同 NpcLabor 方案，见 §7） |

## 2. 游戏机制研究（nightly 23.335）

### 2.1 视野（FOV）系统
- `Fov.Perform(int _x, int _z, int _range, float power)`（`Elin/Fov.cs`）从原点做
  Bresenham 射线，可见格子写入 `fov.lastPoints`，并给 `cell.light` 加光、给 `cell.isSeen`
  标“已探索”。
- PC 视野每帧/移动时经 `Card.CalculateFOV()` → `fov.Perform(pos.x, pos.z, …)` 重算。
- **玩家“看到什么”**由 `Chara.CanSee(Card)` 决定：`IsPC` 分支只查
  `fov.lastPoints.ContainsKey(c.pos.index)`。所以把 FOV 原点挪到拐角后，拐角后的敌人
  自然进入 `lastPoints` → 可见、可被 `CanSee` 判定、小地图也会刷新。
- `Card.CreateFov()` 会给 PC 的 fov 设置 `isPC = true` —— 补丁只用 `__instance.isPC`
  即可精确命中 PC 自己的视野，不影响怪物/光源的 fov 计算。

### 2.2 敌人为什么“不会被发现”
- 敌人发现玩家走的是**敌人的**视线，与玩家视野无关：
  `Chara.CanSeeLos(Card c)` / AI 的目标判定用墙挡视线。
- 窥视只改了“玩家看到什么”，玩家位置没动，敌人视线里玩家仍被墙挡住。
- 更保险的一步：窥视期间把 `EClass.game.gameSpeedIndex = 0`（游戏自带“暂停”档，
  `GameSpeeds = {0,1,2,5}`）。此时 `Core.gameDelta = delta * gameSpeed = 0`，
  `GameUpdater.CharaUpdater` 的回合计时不再累积 → 全图敌人无法行动，自然“没有发现你”。

### 2.3 按钮（ActPlan）
- `ActPlan.Update(PointTarget)` 每帧为当前悬停目标构建动作按钮（`planLeft`=左键栏、
  `planAll`=全动作菜单）。目标是 PC 自己时 `dist == 0`。
- `ActPlan.TrySetAct(Act, Card)` 往按钮列表追加一个 Act。点击后 `Act.Perform()` 执行；
  返回 `true` 会消耗回合，窥视应返回 `false`（CDDA 里窥视不耗时间）。
- 自定义 `Act` 子类只需覆写 `ID`/`TargetType`/`Perform`/`GetText`；`GetSprite` 返回
  null 即可（纯文字按钮）。

### 2.4 暂停
- 直接改 `gameSpeedIndex` 是游戏自带的速度机制（`HotItemSpeed` 的 0 档就是暂停），
  输入仍然有效（玩家可以点速度按钮解暂停），因此窥视期间移动鼠标、按 Esc 都没问题。

## 3. 实现（ElinPeek，位于本目录）

```
ElinPeek.csproj     官方 ElinPluginTemplate 模板工程（netstandard2.0）
Plugin.cs           入口：Harmony.CreateAndPatchAll
PeekManager.cs      状态机：进入/退出/每帧刷新 + 拐角原点计算
ActPeek.cs          「窥视」按钮 Act
PeekText.cs         CN/EN/JP 三语词典 + T(key) 解析
Patches.cs          5 个补丁（见下）
package/package.xml 包元数据（id: deux.peek）
```

### 补丁清单
1. **`Fov.Perform`（Prefix）**：窥视中把 PC 视野原点 `(_x,_z)` 换成拐角格子；
   开了真实图层时自动退出窥视。
2. **`AM_BaseGameMode.OnUpdateInput`（Prefix）**：窥视中按 Esc 只退出、不弹系统菜单。
3. **`AM_Adv._OnUpdateInput`（Prefix+Postfix）**：窥视中吞掉普通输入，右键/左键退出；
   Postfix 每帧 `RecalculateFOV()` 让视野跟随鼠标实时移动。
4. **`Scene.OnUpdate`（Postfix）**：安全网——切模式/开层/死亡/回标题时强制退出并解冻。
5. **`ActPlan.Update`（Postfix）**：目标为 PC 自己（`dist==0`）时注入 `ActPeek` 按钮。

### 拐角原点算法（CDDA 式）
- 由鼠标相对 PC 的方向取 8 向之一。
- 优先用正前方格子：能站 → 等于“探出一步”的视野。
- 正前方是墙（`cell.blockSight`）→ 尝试左右两个斜角格子，选第一个能站的：
  即“绕到墙角后”的视野。
- 鼠标回到 PC 身上 → 不重定向，回到正常视野。

## 4. 已构建产物
`dotnet build` 直接输出到
`E:\SteamLibrary\steamapps\common\Elin\Package\Mod_ElinPeek\`（DLL + package.xml），
游戏内启用该 Mod 即可加载（BepInEx 已装的前提下）。

## 5. 待游戏内验证（开放风险）
1. **敌人渲染裁剪**：理论上 `fov.lastPoints` 驱动可见性，但敌人/物品渲染的具体裁剪
   逻辑未逐行确认。若窥视后敌人仍不显示，需要额外强制刷新敌人 renderer
   （`c.renderer`/`isSynced`）。最可能在真实地图上验证。
2. **点击 PC 原有按钮变化**：若 PC 格原本已有动作（如“搜索”），加入「窥视」后左键
   点击会变成多动作上下文菜单（含窥视），属预期内的轻微 UI 变化。
3. **暂停副作用**：`gameSpeedIndex=0` 只冻结角色回合；昼夜/地表特效仍走 `delta`，
   窥视是短时操作，影响可忽略。
4. **鼠标悬停 UI 上时** `mouseTarget.pos` 保持上一帧地图格，视野不会乱跳。

## 6. 后续可扩展（CDDA 特色）
- 透过**窗户/关着的门**窥视（不要求 `!blockSight`，改为查 `hasWindow`/`hasDoor`）。
- 窥视原点高亮（`Point.SetHighlight` / `tileMap.passGuideFloor`）。
- 可选热键直接进入窥视（不经过按钮）。
- 配置项：窥视是否暂停世界、是否消耗微量时间、窥视距离。
- 音频：进入/退出反馈音已用 `SE.ClickOk()` / `SE.CancelAction()`，可换自定义音效。

## 7. 多语言（CN / EN / JP）
采用与 NpcLabor 相同的轻量方案（参考 `D:\work\Elin\5\2\NpcLabor\LaborText.cs`）：
- `PeekText.cs` 内三张词典 `Cn` / `En` / `Ja`，CN 为默认。
- `T(key)` 解析顺序：当前语言表 → En → Cn → 原 key。
- 语言判定直接用 `Lang.isEN` / `Lang.isJP` / `Lang.langCode`（本 mod 直接引用 Elin
  程序集编译，无需像 NpcLabor 那样用反射兜底）。
- 当前文本：`peek.button`（按钮名）、`peek.hint.enter`（进入提示）。
- 为什么不用 `LangMod/SourceLocalization.json`：该文件的键是**源表行键**
  （`SourceChara.<id>.<字段>` 这种），只喂给源表行，**不会**进 `Lang.Get` 的通用文本
  表 `Lang.General`；而手写 `General.xlsx` 又重，所以代码内词典更合适。
