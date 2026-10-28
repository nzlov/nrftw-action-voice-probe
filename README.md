# NRFTW Action Voice Probe

`NRFTW Action Voice Probe` 是一个用于《No Rest for the Wicked》的 MelonLoader IL2CPP Mod。它会捕捉角色动作、战斗信号、符文技能、道具使用和 Wwise 播放事件，并按 `bindings.ini` 中的绑定触发自定义 Wwise soundbank 里的事件。

当前项目重点是动作探测和语音触发，不修改游戏存档、数值或网络逻辑。

仓库仅包含插件源码、配置和构建工具。Wwise 工程、原始音频与生成的 soundbank 在本地维护，整个 `ActionVoiceProbe/` 目录不纳入版本管理；克隆仓库后需自行准备兼容的内容 bank 才能打包音频。

## 当前状态

- 插件框架：MelonLoader IL2CPP
- 目标框架：`.NET 6.0`
- Harmony ID：`local.nrftw.actionvoiceprobe`
- 插件名：`NRFTW Action Voice Probe`
- 插件版本：`0.1.0`
- 音频格式：Wwise `.bnk` 中的 event
- 配置格式：INI
- 配置加载：游戏启动时解析 `bindings.ini`，首次播放时加载自定义 Wwise `.bnk`
- DLL 更新：需要重启游戏生效

## 目录结构

源码目录：

```text
<project>/
├── ActionVoiceProbePlugin.cs
├── BINDING_KEYS.md
├── bindings.ini
├── scripts/
├── nrftw-action-voice-probe.csproj
├── bin/
└── obj/
```

游戏安装目录：

```text
<game>/
```

运行时目录：

```text
<game>/
└── Mods/
    └── NRFTW.ActionVoiceProbe/
        ├── NRFTW.ActionVoiceProbe.dll
        ├── manifest.json
        ├── bindings.ini
        ├── BINDING_KEYS.md
        └── wwise/
            └── ActionVoiceProbe.bnk
```

其中 `<project>` 表示本工具源码目录，`<game>` 表示游戏安装目录。

自定义 Wwise 内容 bank 放在 `Mods/NRFTW.ActionVoiceProbe/wwise/` 下，插件会自动扫描并加载该目录中的 `.bnk`，但始终忽略 `Init.bnk`。绑定 key 的详细说明见 `BINDING_KEYS.md`。

## 前置条件

1. 游戏已安装并能在 Linux/Proton 下正常启动。
2. 游戏目录中已安装可用的 MelonLoader IL2CPP。
3. MelonLoader 已生成 `MelonLoader/Il2CppAssemblies/` 下的互操作 DLL。
4. 本机存在可用的 .NET SDK。

本项目当前 `csproj` 直接引用游戏目录下的 MelonLoader 和 interop DLL：

```text
<game>/MelonLoader/net6/
<game>/MelonLoader/Il2CppAssemblies/
```

如果游戏安装路径变化，可以在构建时传入 `GameDir`：

```bash
dotnet build <project>/nrftw-action-voice-probe.csproj -c Release -p:GameDir=<game>
```

## 构建

使用本机已准备好的 .NET SDK：

```bash
dotnet build <project>/nrftw-action-voice-probe.csproj -c Release
```

构建产物：

```text
<project>/bin/Release/net6.0/nrftw-action-voice-probe.dll
```

## 打包

编译插件本身不依赖 Wwise 工程或音频文件。打包前，请自行提供符合下文“音频格式要求”的 Windows 内容 bank。

默认从本地 `ActionVoiceProbe/GeneratedSoundBanks/Windows/ActionVoiceProbe.bnk` 读取：

```bash
make package
```

也可指定其他位置的内容 bank：

```bash
make package WWISE_BANK_SOURCE=/path/to/ActionVoiceProbe.bnk
```

打包命令会先构建插件，再校验并复制 bank，生成 `dist/NRFTW.ActionVoiceProbe.zip`。

## 安装

创建 Mod 目录：

```bash
install -d <game>/Mods/NRFTW.ActionVoiceProbe/wwise
```

复制 DLL 到 Mod 目录：

```bash
cp \
  <project>/bin/Release/net6.0/nrftw-action-voice-probe.dll \
  <game>/Mods/NRFTW.ActionVoiceProbe/NRFTW.ActionVoiceProbe.dll
```

安装默认配置：

```bash
cp \
  <project>/bindings.ini \
  <game>/Mods/NRFTW.ActionVoiceProbe/bindings.ini
```

复制绑定说明：

```bash
cp \
  <project>/BINDING_KEYS.md \
  <game>/Mods/NRFTW.ActionVoiceProbe/BINDING_KEYS.md
```

复制 MelonLoader 子目录 manifest：

```bash
cp \
  <project>/manifest.json \
  <game>/Mods/NRFTW.ActionVoiceProbe/manifest.json
```

安装后启动游戏，MelonLoader 日志中应能看到：

```text
[ActionVoiceProbe] loaded
```

## 配置文件

运行时配置文件路径：

```text
<game>/Mods/NRFTW.ActionVoiceProbe/bindings.ini
```

基础格式：

```ini
[general]
enabled=true
cooldownMs=250
chargeDelayMs=200
fallDelayMs=300
startupDiagnostic=false
exportMap=false
logLevel=none

[actions]
basicAttack=
itemUse=nrftw_action_voice_item_use

[attacks]
attack.normal.1=nrftw_action_voice_normal_1
attack.normal.2=nrftw_action_voice_normal_2

[gestures]
bestialPower=nrftw_action_voice_bestial_power

[runes]
plagueNova=nrftw_action_voice_plague_nova
tremorWave=
```

### general

| 键 | 说明 |
| --- | --- |
| `enabled` | 是否启用 Wwise Event 播放，`true` 或 `false` |
| `cooldownMs` | 同一个绑定键的播放冷却时间，单位毫秒 |
| `chargeDelayMs` | 按住攻击超过此时间且游戏仍报告 `HoldingInput=true` 时播放蓄力开始，单位毫秒 |
| `fallDelayMs` | 持续处于 `fall` Action View 超过此时间后播放坠落过程音效，单位毫秒 |
| `startupDiagnostic` | 启动后自动触发 `basicAttack`，用于验证 bank 加载和 `PostEvent` |
| `exportMap` | 启动后导出武器、符文与全部表情映射到 `map.csv` |
| `logLevel` | 日志打印等级：`none`、`debug`、`info`、`warning` 或 `error` |

### actions

`[actions]` 中每一行都是：

```ini
标准动作名=WwiseEventName1,WwiseEventName2
```

绑定值直接填写 Wwise Event 名，不使用类型前缀。一个绑定可以用英文逗号配置多个 Event，每次触发会随机播放其中一个；只配置一个 Event 时行为不变。插件会自动加载 `Mods/NRFTW.ActionVoiceProbe/wwise/` 目录下除 `Init.bnk` 外的内容 bank，事件名必须存在于其中某个自定义 soundbank 中。

示例：

```ini
basicAttack=nrftw_action_voice_basic_attack_1,nrftw_action_voice_basic_attack_2
chargedAttackStart=nrftw_action_voice_charge_start
parryFailed=nrftw_action_voice_parry_fail
blockSuccess=nrftw_action_voice_block_success
shoulderBarge=nrftw_action_voice_shoulder_barge
playerLevelUp=nrftw_action_voice_player_level_up
```

`basicAttack` 是本地玩家执行基础攻击 Action 时的兜底 Event，不要求动作存在 Windup 阶段。更精确的武器攻击 key 写在 `[attacks]` 中，匹配顺序为：

```text
weapon.<weaponId>.<attackType>.<comboIndex>
weapon.<weaponName>.<attackType>.<comboIndex>
attack.<attackType>.<comboIndex>
attack.<attackType>
attackId.<actionId>
basicAttack
```

Action View 是否为普攻只以游戏的 `ActionData.ActionType` 为准；符文语音由独立的 `OnHeroExecutedSpecial` 信号负责，动作是否被符文资产引用不会改变普攻分类。

`comboIndex` 从 `1` 开始。`attack.<attackType>` 会在精确连段 key 未配置时作为同类攻击的通用回退。`attackType`、`weaponName` 和符文 key 说明见 `BINDING_KEYS.md`。

所有事件统一通过 `nrftw_action_voice_pitch_cents` RTPC 调整 Voice Pitch。武器攻击和蓄力事件读取当前 View 的有效动画速度，符文事件读取 `ActionData.TimeScale`，其他事件的动作基础速率为 `1.0`。最终速率为 `动作基础速率 * 配置倍率`，并限制在 `0.25–4.0`；Pitch 换算公式为 `1200 * log2(最终速率)`。动作速率读取失败时回退到 `1.0`。

配置倍率写在 `[audio]` 中。`rate.default` 应用于所有事件，`rate.<bindingKey>` 按最终命中的绑定 key 覆盖默认值：

```ini
[audio]
rate.default=1.0
rate.basicAttack=0.95
rate.plagueNova=1.10
rate.playerDeath=0.90
```

例如武器攻击最终回退到 `basicAttack` 时使用 `rate.basicAttack`；精确命中 `attack.normal.1` 时只查 `rate.attack.normal.1`，未配置则使用 `rate.default`。

### gestures

`[gestures]` 中的 key 使用表情动作名。插件在游戏确认表情执行时播放，只处理本地玩家；打开表情界面或发送使用请求不会触发：

```ini
[gestures]
bestialPower=nrftw_action_voice_bestial_power
```

“凶残之力”的内部动作名是 `bestialPower`。运行时无法解析表情资产名时，插件会通过全部表情资产建立的 Action ID 映射回退；`map.csv` 的 `assetGuid` 是表情数据 ID，`actionId` 是执行动作 ID。其他表情名可通过 `exportMap=true` 导出，在 `map.csv` 中查找 `type=gesture` 的行。

### runes

`[runes]` 中的 key 直接使用游戏内符文技能名：

```ini
plagueNova=nrftw_action_voice_plague_nova
plagueBreath=nrftw_action_voice_plague_breath
tremorWave=nrftw_action_voice_tremor_wave
```

默认 `bindings.ini` 已列出全部内置符文技能名，并按 `nrftw_action_voice_<snake_case>` 规则填入对应 Event 名。游戏启动后，插件会用内置中文元数据同步对应 key 上方的 `; 中文名：...` 注释；已加载的 `HeroRuneDataAsset` 可用时以游戏当前名称覆盖，且不会修改 Event 配置值。Event 必须存在于成功加载的自定义 soundbank 中才会播放。

## 标准动作名

| 配置名 | 说明 |
| --- | --- |
| `basicAttack` | 本地玩家进入游戏判定为基础攻击的 Action View |
| `chargedAttackStart` | 超过蓄力阈值且输入仍被按住 |
| `chargedAttackStop` | Windup 最终进入蓄力攻击 View |
| `dodgeRoll` | 翻滚开始 |
| `quickstep` | 快步开始 |
| `itemUse` | 使用道具或消耗品 |
| `backstab` | 本地玩家执行背刺动作 |
| `shoulderBarge` | 本地玩家开始肩撞动作，挥空也会触发 |
| `playerLevelUp` | 本地玩家角色等级实际提升 |
| `playerFall` | 本地玩家持续坠落 |
| `playerDeath` | 本地玩家死亡 |
| `enemyDeath` | NPC/敌方死亡 |
| `parryAttempt` | 招架尝试候选 |
| `parryFailed` | 招架动作完成且本次未收到成功信号 |
| `parrySuccess` | 招架成功 |
| `blockStart` | 格挡开始候选 |
| `blockEnd` | 格挡结束候选 |
| `blockSuccess` | 格挡成功 |

`playerDeath` 支持原因键，并按以下顺序匹配：

```text
playerDeath.fall
playerDeath.backstab
playerDeath.fatality
playerDeath.critical
playerDeath.rune
playerDeath.<heat|cold|electric|plague|bleed|physical>
playerDeath
```

## 常用绑定示例

```ini
[actions]
basicAttack=nrftw_action_voice_basic_attack
chargedAttackStart=nrftw_action_voice_charge_start
chargedAttackStop=nrftw_action_voice_charge_stop
dodgeRoll=nrftw_action_voice_dodge
quickstep=nrftw_action_voice_quickstep
itemUse=nrftw_action_voice_item_use
backstab=nrftw_action_voice_backstab
playerFall=nrftw_action_voice_player_fall
playerDeath.fall=nrftw_action_voice_player_death_fall
playerDeath.backstab=nrftw_action_voice_player_death_backstab
playerDeath.fatality=nrftw_action_voice_player_death_fatality
playerDeath.critical=nrftw_action_voice_player_death_critical
playerDeath.rune=nrftw_action_voice_player_death_rune
playerDeath.heat=nrftw_action_voice_player_death_heat
playerDeath.cold=nrftw_action_voice_player_death_cold
playerDeath.electric=nrftw_action_voice_player_death_electric
playerDeath.plague=nrftw_action_voice_player_death_plague
playerDeath.bleed=nrftw_action_voice_player_death_bleed
playerDeath.physical=nrftw_action_voice_player_death_physical
playerDeath=nrftw_action_voice_player_death
enemyDeath=nrftw_action_voice_enemy_death

parryAttempt=nrftw_action_voice_parry_attempt
parryFailed=nrftw_action_voice_parry_fail
parrySuccess=nrftw_action_voice_parry_success

blockStart=nrftw_action_voice_block_start
blockEnd=nrftw_action_voice_block_end
blockSuccess=nrftw_action_voice_block_success

[attacks]
attack.normal=nrftw_action_voice_normal_fallback
attack.normal.1=nrftw_action_voice_normal_1
attack.normal.2=nrftw_action_voice_normal_2
attack.plunge=nrftw_action_voice_ground_slam

[runes]
plagueNova=nrftw_action_voice_plague_nova
plagueBreath=nrftw_action_voice_plague_breath
tremorWave=nrftw_action_voice_tremor_wave
skyfallSmash=nrftw_action_voice_skyfall_smash
spiritBurst=nrftw_action_voice_spirit_burst
```

## 已知动作和技能名

绑定 key、`attackType`、`weaponName` 和已确认符文名见 `BINDING_KEYS.md`。

当前日志中确认过的招架动作：

| 类型 | AssetGuid | AssetName | BakedViewName |
| --- | --- | --- | --- |
| 空手招架 | `1AA1190B35A0497B` | `UnarmedParry` | `UnarmedParryView(Clone)` |
| 盾牌招架 | `21B5F5DBD4603F88` | `smallShieldParryData` | `parry` |

道具使用当前可捕捉到：

```text
itemUse
```

以及动作视图：

```text
action.start: BakedViewName=useTimeline
```

道具实体名目前未稳定解析出来，因此建议先绑定标准动作名：

```ini
itemUse=nrftw_action_voice_item_use
```

## 内部事件来源

配置文件不直接使用这些内部 hook 名。它们只用于说明标准动作名来自哪些游戏事件。

| 标准配置名 | 来源 | 说明 |
| --- | --- | --- |
| `basicAttack` | `Moon.Forsaken.ActionView.OnStartView` | `ActionData.ActionType` 被游戏判定为基础攻击；不要求存在 `*WindupView` |
| `chargedAttackStart` | `ActionRuntime.HoldingInput` | 到达 `chargeDelayMs` 时输入仍被按住 |
| `chargedAttackStop` | `Moon.Forsaken.ActionView.OnStartView` | `*WindupView` 最终切换到同前缀 `*ChargeView`；先停止蓄力开始 playing ID |
| `dodgeRoll` | `Moon.Forsaken.DodgeRollView.OnStartView` | 翻滚开始 |
| `quickstep` | `Moon.Forsaken.QuickStepActionView.OnStartView` | 快步开始 |
| `itemUse` | `Quantum.Frame+FrameSignals.OnItemConsumed` | 消耗品使用 |
| `backstab` | `OnPairedActionExecuted` 派生 | 本地玩家作为攻击者执行的 `FinisherPairedActionData.Type=Backstab` |
| `shoulderBarge` | `Quantum.Frame+FrameSignals.OnActionExecuted` | 本地玩家执行 `heroAttackShoulderBargeData` 时立即触发，不等待命中 |
| `playerLevelUp` | `Quantum.Frame+FrameSignals.OnLevelChanged` | 本地玩家的 `currentLevel` 大于 `previousLevel` |
| `playerFall` | `Moon.Forsaken.ActionView.OnStartView/OnStopView` | `fall` 持续超过 `fallDelayMs` 后播放，离开时停止 playing ID |
| `playerDeath` | `Quantum.Frame+FrameSignals.OnPlayerDied` | 本地玩家死亡，远端玩家会被过滤 |
| `enemyDeath` | `Quantum.Frame+FrameSignals.OnNpcDied` | NPC/敌方死亡 |
| `parryAttempt` | `OnActionExecuted` 派生 | 招架尝试候选，基于 action 描述包含 `parry` |
| `parryFailed` | `OnActionCompleted` 派生 | action 描述包含 `parry`，且本次未收到 `OnPlayerParry` 成功信号 |
| `parrySuccess` | `Quantum.Frame+FrameSignals.OnPlayerParry` | 招架成功 |
| `blockStart` | `OnActionExecuted` 派生 | 格挡开始候选，基于 action 描述包含 `block` |
| `blockEnd` | `OnActionCompleted` 派生 | 格挡结束候选，基于 action 描述包含 `block` |
| `blockSuccess` | `Quantum.Frame+FrameSignals.OnPlayerBlock` | 格挡成功 |

带 `candidate` 的事件是从动作描述中推断出来的候选事件，不等同于游戏底层明确宣告的成功信号。

## 音频格式要求

当前只支持 Wwise 自定义 soundbank。

在自行维护的 Wwise 2022.1 工程中生成 Windows 内容 bank。`action_voice_root` 必须直接输出到 ShortID 为 `610960780` 的 `VOX_Player`，并使用 `nrftw_action_voice_pitch_cents` RTPC。执行 `make package` 时会验证 bank 版本、Bus 和 RTPC 引用，然后直接复制生成的内容 bank。运行时文件位于：

```text
dist/NRFTW.ActionVoiceProbe/Mods/NRFTW.ActionVoiceProbe/wwise/ActionVoiceProbe.bnk
```

只部署内容 bank，不要部署 Wwise 工程生成的 `Init.bnk`。`VOX_Player` 和 Pitch Game Parameter 的本地定义用于 Wwise 编辑与生成；运行时继续使用游戏的 Init bank。动作语音会进入游戏的玩家语音 Bus；是否跟随对话音量、ducking 和暂停状态仍需在游戏中验证。测试 bank 里的音频先设为非 streaming / in-memory。插件加载自定义 bank 后会把 Wwise base path 恢复到游戏原始目录，避免影响游戏后续加载自己的 soundbank。

配置示例：

```ini
[general]
startupDiagnostic=true
logLevel=info

[actions]
basicAttack=nrftw_action_voice_basic_attack
```

启动后会记录：

```text
[ActionVoiceProbe] audio.wwise.basePath.set: ...
[ActionVoiceProbe] audio.wwise.bank.load: ...
[ActionVoiceProbe] audio.wwise.post: ...
[ActionVoiceProbe] audio.pitch: hook=... key=... actionRate=... configuredRate=... finalRate=... cents=...
```

不能直接绑定：

- `.wav`
- `.mp3`
- `.flac`
- Wwise `.wem`

如果需要替换同名 Wwise bank，重启游戏后才会重新加载。

## 日志

MelonLoader 日志：

```text
<game>/MelonLoader/Latest.log
```

Unity Player 日志：

```text
<proton-prefix>/drive_c/users/steamuser/AppData/LocalLow/Moon Studios/NoRestForTheWicked/Player.log
```

常见插件日志：

```text
[ActionVoiceProbe] loaded
[ActionVoiceProbe] patched ...
[ActionVoiceProbe] audio.config.loaded: configured=...
[ActionVoiceProbe] audio.wwise.config: basePath=... bankScan=*.bnk ...
[ActionVoiceProbe] audio.wwise.bank.load: bank=... result=... loaded=...
[ActionVoiceProbe] audio.wwise.post: key=... event=...
[ActionVoiceProbe] audio.startupDiagnostic.begin: ...
```

动作日志每个 hook 默认最多输出 30 次，避免日志刷屏。hook 触发时会做 key 匹配和冷却判断，然后通过 Wwise `PostEvent` 触发事件。插件首次触发 Wwise 事件时会扫描并加载 `wwise/` 目录下的全部 `.bnk`。

`logLevel=none` 会关闭插件日志；`debug` 会打印动作探测明细；`info` 只打印加载和播放摘要；`warning` 只打印警告；`error` 只打印异常。

## 排错

### 游戏启动失败

先移除或改名插件 DLL：

```bash
mv \
  <game>/Mods/NRFTW.ActionVoiceProbe/NRFTW.ActionVoiceProbe.dll \
  <game>/Mods/NRFTW.ActionVoiceProbe/NRFTW.ActionVoiceProbe.dll.disabled
```

然后重新启动游戏，确认是否由插件导致。

同时检查：

```text
MelonLoader/Latest.log
Player.log
```

### 没有播放音频

检查顺序：

1. `bindings.ini` 是否在运行时目录。
2. `[general] enabled=true`。
3. `[general] logLevel=debug` 时，绑定键是否能在 MelonLoader 日志中看到。
4. `wwise/` 目录下是否存在 `.bnk`。
5. 日志中的 `audio.wwise.bank.load` 是否为 `AK_Success` 或 `AK_BankAlreadyLoaded`。
6. 绑定值是否为 `EventName` 或英文逗号分隔的 Event 列表，且事件名存在于已加载 bank。
7. 是否被 `cooldownMs` 冷却限制。

### 配置修改没有生效

`bindings.ini` 只在游戏启动时加载。修改配置或替换音频后，需要重启游戏。启动后查看日志中是否出现：

```text
[ActionVoiceProbe] audio.config.loaded
```

如果 bank 成功加载，还会出现：

```text
[ActionVoiceProbe] audio.wwise.bank.load
[ActionVoiceProbe] audio.wwise.post
```

### 某些动作没有精确区分

当前配置入口保留三类 key：

```ini
[actions]
basicAttack=nrftw_action_voice_basic_attack

[attacks]
attack.normal.1=nrftw_action_voice_normal_1

[gestures]
bestialPower=nrftw_action_voice_bestial_power

[runes]
plagueNova=nrftw_action_voice_plague_nova
```

如果某个动作没有对应标准动作名，需要先在插件里新增标准名映射，再写入 `bindings.ini`。不再通过内部 hook 名直接配置 Event。

## 开发注意事项

- 不要重新加入 `Quantum.InputBlockingActionData.OnStartedBlocking` hook。该 hook 曾导致启动阶段 `AccessViolationException`。
- `blockStart` 和 `blockEnd` 是安全路径上的候选判断，不是直接 hook `InputBlockingActionData`。
- `parrySuccess` 和 `blockSuccess` 依赖游戏是否触发 `OnPlayerParry` / `OnPlayerBlock`。
- 新增 hook 后必须先验证游戏能启动到主界面，再验证动作日志。
- 修改 `bindings.ini`、替换 `.bnk` 或替换 DLL 后，都需要重启游戏。

## 快速验证流程

1. 构建 Release DLL。
2. 复制 DLL 到 `Mods/NRFTW.ActionVoiceProbe/NRFTW.ActionVoiceProbe.dll`。
3. 复制 `manifest.json` 到 `Mods/NRFTW.ActionVoiceProbe/manifest.json`。
4. 复制 `bindings.ini` 和 `BINDING_KEYS.md` 到 `Mods/NRFTW.ActionVoiceProbe/`。
5. 把打包产物中的 `ActionVoiceProbe.bnk` 放入 `Mods/NRFTW.ActionVoiceProbe/wwise/`，不要放入 `Init.bnk`。
6. 启动游戏。
7. 如果需要查看日志，把 `[general] logLevel` 临时改为 `debug`。
8. 查看 MelonLoader 日志是否出现 `[ActionVoiceProbe] loaded`。
9. 在游戏中执行攻击、翻滚、快步、招架、格挡、使用道具、表情或符文技能。
10. 查看日志是否出现对应 hook、`audio.wwise.bank.load` 和 `audio.wwise.post`。

如果出现 `audio.wwise.post.failed` 且 `playingId=0`，说明 Wwise 没有接受该 Event，优先检查 Event 名是否存在于已加载的自定义 soundbank。

如需生成武器中文名、`weaponName`、符文中文名、`runeName` 和全部表情映射：

```ini
[general]
exportMap=true
```

启动或重启游戏并进入会话后，插件会从 Quantum 资产数据库写出 `Mods/NRFTW.ActionVoiceProbe/map.csv`。如果会话或资产数据库尚不可用，插件不会用空文件覆盖已有映射。导出完成后可将 `exportMap` 改回 `false`，避免后续启动时重复覆盖文件。

## 卸载

删除或改名插件 DLL：

```bash
rm <game>/Mods/NRFTW.ActionVoiceProbe/NRFTW.ActionVoiceProbe.dll
```

如需同时删除配置和 Wwise bank：

```bash
rm -r <game>/Mods/NRFTW.ActionVoiceProbe
```

删除前请确认目录中没有需要保留的配置或 Wwise bank。

## 许可证

本项目采用 [Apache License 2.0](LICENSE) 许可证。
