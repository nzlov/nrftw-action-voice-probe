# Binding Keys

`bindings.ini` 只保留可直接填写的基础配置。键名说明、攻击 key 规则和符文说明放在这里。

绑定值直接填写 Wwise event 名。多个 Event 使用英文逗号分隔，每次触发随机播放一个：

```ini
key=EventName1,EventName2
```

修改 `bindings.ini`、替换 DLL 或替换 `.bnk` 后需要重启游戏。

## 基础动作

| 配置名 | 说明 |
| --- | --- |
| `basicAttack` | 本地玩家执行基础攻击时的兜底音效 |
| `chargedAttackStart` | 按住攻击超过蓄力阈值 |
| `chargedAttackStop` | 超过蓄力阈值后松开攻击 |
| `dodgeRoll` | 翻滚动作开始 |
| `quickstep` | 快步动作开始 |
| `itemUse` | 使用道具或消耗品 |
| `backstab` | 本地玩家执行背刺动作 |
| `shoulderBarge` | 本地玩家开始肩撞动作，挥空也会触发 |
| `playerLevelUp` | 本地玩家角色等级实际提升 |
| `playerFall` | 本地玩家持续坠落超过 `fallDelayMs` |
| `playerDeath` | 本地玩家死亡 |
| `enemyDeath` | NPC/敌方死亡 |
| `parryAttempt` | 招架尝试候选 |
| `parryFailed` | 招架动作完成且本次未收到成功信号 |
| `parrySuccess` | 招架成功 |
| `blockStart` | 格挡开始候选 |
| `blockEnd` | 格挡结束候选 |
| `blockSuccess` | 格挡成功 |

本地 Action View 的 `ActionData.ActionType` 被游戏判定为基础攻击时播放 `basicAttack`，因此没有 `*Windup` 阶段的武器和连段后续攻击也能触发。`*Windup` Action View 仅用于计算 `chargeDelayMs`；到达阈值时仅在 `ActionRuntime.HoldingInput=true` 时播放 `chargedAttackStart`，切换到同前缀 `*ChargeView` 时停止蓄力开始 playing ID 并播放 `chargedAttackStop`。
这三个绑定不监听 `MeleeEffects` 或 `StartAttack`。Action View 是否为普攻只以游戏的 `ActionData.ActionType` 为准；符文语音由独立的 `OnHeroExecutedSpecial` 信号负责，动作是否被符文资产引用不会改变普攻分类。取消 Windup 也不会遗留待播放状态。

`playerFall` 在本地玩家进入 `fall` Action View 并持续超过 `fallDelayMs` 后播放；离开 `fall` 时按 Wwise playing ID 停止。普通短距离下落会在阈值前取消，不影响独立的 `playerDeath.fall` 死亡音效。

所有绑定都经过统一速率计算：武器攻击和蓄力取 View 动画速度，符文取 `ActionData.TimeScale`，其他事件以 `1.0` 为动作基础速率；再乘以 `[audio]` 中的配置倍率。

```ini
[audio]
rate.default=1.0
rate.basicAttack=0.95
rate.plagueNova=1.10
```

`rate.<bindingKey>` 使用最终命中的事件 key，并覆盖 `rate.default`。最终速率限制在 `0.25–4.0`；无效、非正数配置会被忽略。

玩家死亡按下列顺序匹配，命中第一个已配置的 key 后播放：

```text
playerDeath.fall
playerDeath.backstab
playerDeath.fatality
playerDeath.critical
playerDeath.rune
playerDeath.<heat|cold|electric|plague|bleed|physical>
playerDeath
```

坠落死亡通过伤害代理的 `Fall` 标志识别。同一次死亡可能同时具有多个标志，例如背刺暴击火焰伤害。更具体的键未配置时，会继续匹配后续原因并最终回退到 `playerDeath`。

## 武器攻击

武器攻击触发时按下面顺序匹配，命中第一个已配置的 key 后播放：

```text
weapon.<weaponId>.<attackType>.<comboIndex>
weapon.<weaponName>.<attackType>.<comboIndex>
attack.<attackType>.<comboIndex>
attack.<attackType>
attackId.<actionId>
basicAttack
```

`comboIndex` 从 `1` 开始。`attack.<attackType>` 会在精确连段 key 未配置时作为同类攻击的通用回退。`weaponName`、`weaponId`、`actionId` 会在运行时从游戏数据读取；如果解析不到武器名，优先使用 `weaponId` 或 `attackId`。

示例：

```ini
[attacks]
weapon.2ab34c.normal.1=nrftw_action_voice_sword_1
weapon.sacrament-blade.normal.2=nrftw_action_voice_sword_2
attack.normal.1=nrftw_action_voice_normal_1
attack.normal=nrftw_action_voice_normal_fallback
attackId.1aa1190b35a0497b=nrftw_action_voice_specific_attack
```

### attackType

| attackType | 说明 |
| --- | --- |
| `normal` | 普通站立攻击连段 |
| `run` | 奔跑攻击 |
| `quickstep` | 快步后派生攻击 |
| `roll` | 翻滚后派生攻击 |
| `knockdown` | 倒地或击倒相关攻击分支 |
| `plunge` | 下落攻击 |
| `parry` | 招架后派生攻击 |

## 表情

表情绑定写在 `[gestures]` 中，key 使用 `map.csv` 中 `type=gesture` 行的 `name`：

```ini
[gestures]
bestialPower=nrftw_action_voice_bestial_power
```

插件只在 `OnPlayerPerformedGestureDirector` 信号中触发表情音效，并使用信号携带的表情 Action 资产名生成内部 key；运行时无法解析资产名时，会使用全部表情资产建立的 Action ID 映射回退。`map.csv` 的 `assetGuid` 是表情数据 ID，`actionId` 是执行动作 ID。这里只播放本地玩家实际执行成功的表情；打开表情界面或发送使用请求不会触发。

## 符文

符文绑定写在 `[runes]` 中，key 使用游戏内部符文名：

```ini
[runes]
plagueNova=nrftw_action_voice_plague_nova
tremorWave=nrftw_action_voice_tremor_wave
```

插件只在 `OnHeroExecutedSpecial` 信号中触发符文语音，并从运行时 `HeroRuneDataAsset.Actions` 建立完整的 actionId 到符文 key 映射。默认 `bindings.ini` 已列出全部内置符文 key，并按 `nrftw_action_voice_<snake_case>` 规则填入对应 Event 名。

游戏启动后，插件会用内置中文元数据同步 key 上方的 `; 中文名：...` 注释；已加载资产可用时以游戏当前名称覆盖，已有 Event 配置值和其他注释保持不变。

## 调试

需要确认实际生成的攻击 key 时，把配置改为：

```ini
[general]
logLevel=debug
```

然后进入游戏执行对应攻击，查看：

```text
<game>/MelonLoader/Latest.log
```

攻击触发时会输出候选 key：

```text
[ActionVoiceProbe] audio.keys: hook=weapon.attack.start keys=attack.normal.1,attack.normal,attackId....
```

## 导出 map.csv

在运行时 `bindings.ini` 的 `[general]` 中启用导出：

```ini
exportMap=true
```

启动或重启游戏并进入会话后，插件会从 Quantum 资产数据库读取武器、符文和全部表情资产，并写出：

```text
Mods/NRFTW.ActionVoiceProbe/map.csv
```

导出完成后可将 `exportMap` 改回 `false`，避免后续启动时重复覆盖文件。

CSV 字段：

| 字段 | 说明 |
| --- | --- |
| `type` | `weapon`、`rune` 或 `gesture` |
| `chineseName` | 简体中文显示名 |
| `name` | `weaponName`、`runeName` 或表情配置 key |
| `assetGuid` | 资产 GUID |
| `actionId` | 表情执行动作 ID；其他类型为空 |
| `assetObjectName` | Unity 资产对象名 |
| `localizationId` | 本地化消息 ID |
