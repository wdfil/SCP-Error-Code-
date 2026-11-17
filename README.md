# 感应卡 · DoorKeycardFromInventory

背包里的门禁卡**不用拿在手上**也能开门、开储物柜。

SCP:Erro Code 里开一扇门必须先把对应等级的门禁卡切到手上，战斗中切卡既慢又容易被打。
这个插件让服务器"感应"你背包里的卡：只要背包里有权限足够的卡，站在门前按 E 就能直接开。

适用于 CarLX 插件框架（`CarLX 6.1.0` 及以上）。

## 功能

| 功能 | 说明 |
|---|---|
| 背包开门 | 背包里有匹配权限的门禁卡即可开门，无需手持 |
| 背包开柜 | 储物柜（Locker）同样支持 |
| `RequireAll` 语义 | 门要求"全部权限位"时，卡必须覆盖所有位；否则任一匹配位即可 |
| SCP 角色隔离 | SCP 角色不能借用背包里的卡 |
| SCP 物品柜限制 | 装 SCP 物品的柜子屏蔽科学家卡 / 区域总监卡 / 警卫卡，只能用高级卡 |
| 只读背包 | 不切换手持物品、不移动/丢弃任何物品 |

## 实现原理

两个 Harmony 补丁，只改"权限判定结果"，不碰任何物品状态：

```csharp
// 门：DoorPermissions.CheckPermissions(ItemBase, ReferenceHub)
Prefix  → 背包里找到匹配的卡就直接返回 true，跳过原判定
Postfix → 原判定失败时再兜底查一次背包

// 储物柜：Locker.CheckPerms(KeycardPermissions, ReferenceHub)
Prefix / Postfix → 同上，并对 SCP 物品柜额外过滤低级卡
```

找卡逻辑（`FindCard`）：

```csharp
foreach (ItemBase item in hub.inventory.UserInventory.Items.Values)
{
    KeycardItem card = item as KeycardItem;
    if (card == null) continue;
    if (blocked != null && blocked.Contains(card.ItemTypeId)) continue;   // SCP 柜黑名单

    bool match = requireAll
        ? (card.Permissions & required) == required    // 门要求全部权限
        : (card.Permissions & required) != 0;          // 任意一位即可

    if (match) return card;
}
```

## 安装

1. 编译（需要 .NET Framework 自带的 csc，引用 CarLX 的三个 dll 和游戏程序集）：

   ```bat
   csc.exe /nologo /target:library /langversion:5 /optimize+ ^
     /r:CarLX.API.dll /r:CarLX.Events.dll /r:PluginCommon.dll /r:0Harmony.dll ^
     /r:Assembly-CSharp.dll /r:UnityEngine.CoreModule.dll /r:Mirror.dll ^
     /r:CommandSystem.Core.dll /r:netstandard.dll ^
     /out:DoorKeycardFromInventory.dll DoorKeycardFromInventory.cs
   ```

2. 把 `DoorKeycardFromInventory.dll` 放进服务端 `CarLX/Plugins/`。
3. `reload plugins`（或重启服务端）。

## 配置

插件会自动在 `CarLX/Configs/<端口>-config.yml` 写入：

```yaml
doorkeycard:
  # 是否启用"背包门禁卡"
  is_enabled: true
  # 调试输出
  debug: false
```

## 日志

写入 `Logs/doorkeycard.log`，前 20 次开门/开柜会记录用了哪张卡：

```text
13:20:11.482  patched DoorPermissions.CheckPermissions
13:20:11.483  patched Locker.CheckPerms (scientist/zonemanager/guard blocked for SCP lockers)
13:20:11.483  inventory keycards enabled (doors + lockers)
13:25:03.117  door opened with inventory card: KeycardZoneManager
13:25:41.902  card KeycardScientist blocked from SCP locker
```

## 兼容性

| 项目 | 版本 |
|---|---|
| 插件版本 | 2.1.0 |
| 要求 CarLX | ≥ 6.1.0 |
| 目标框架 | .NET Framework 4.8 |
| 依赖 | 0Harmony（CarLX 自带） |

补丁目标方法若在游戏更新后改名，日志会输出 `patch failed`，此时需要同步更新 `AccessTools.Method` 里的方法名。

## 已知边界

- 只处理 `KeycardItem`，自定义物品若不是 `KeycardItem` 子类不会被识别。
- 卡在背包里但已被丢弃/在别人身上不算。
- 门本身不需要权限（`RequiredPermissions == 0`）时不做任何干预。

## 许可

MIT。SCP:SL 及 CarLX 相关版权归各自作者所有。
