using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using CarLX.API.Features;
using CarLX.API.Interfaces;
using CarLX.PluginCommon;
using HarmonyLib;
using Interactables.Interobjects.DoorUtils;
using InventorySystem.Items;
using InventorySystem.Items.Keycards;
using MapGeneration.Distributors;
using PlayerRoles;

namespace CarLX.DoorKeycardFromInventory
{
    public sealed class DoorKeycardConfig : IConfig
    {
        [Description("Allow keycards in the inventory to open doors and lockers.")]
        public bool IsEnabled { get; set; }

        public bool Debug { get; set; }

        public DoorKeycardConfig()
        {
            IsEnabled = true;
            Debug = false;
        }
    }

    public sealed class DoorKeycardPlugin : Plugin<DoorKeycardConfig>
    {
        private static bool _active;
        private static string _logPath;
        private static int _opens;
        private static Harmony _harmony;
        private static string _harmonyId;

        private static readonly System.Collections.Generic.HashSet<ItemType> ScpLockerBlockedCards =
            new System.Collections.Generic.HashSet<ItemType>
            {
                ItemType.KeycardScientist,
                ItemType.KeycardZoneManager,
                ItemType.KeycardGuard
            };

        public override string Name { get { return "DoorKeycardFromInventory"; } }
        public override string Prefix { get { return "doorkeycard"; } }
        public override string Author { get { return "CarLX"; } }
        public override Version Version { get { return new Version(2, 1, 0); } }
        public override Version RequiredCarLXVersion { get { return new Version(6, 1, 0); } }

        public override void OnEnabled()
        {
            if (Config == null || !Config.IsEnabled)
            {
                base.OnEnabled();
                return;
            }

            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "doorkeycard.log");
            _active = true;
            _opens = 0;

            try
            {
                _harmonyId = "doorkeycard.inv." + Guid.NewGuid().ToString("N");
                _harmony = new Harmony(_harmonyId);

                MethodInfo doorMethod = AccessTools.Method(
                    typeof(DoorPermissions),
                    "CheckPermissions",
                    new Type[] { typeof(ItemBase), typeof(ReferenceHub) });

                MethodInfo lockerMethod = AccessTools.Method(
                    typeof(Locker),
                    "CheckPerms",
                    new Type[] { typeof(KeycardPermissions), typeof(ReferenceHub) });

                MethodInfo doorPrefix = typeof(DoorKeycardPlugin).GetMethod("DoorPrefix", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo doorPostfix = typeof(DoorKeycardPlugin).GetMethod("DoorPostfix", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo lockerPrefix = typeof(DoorKeycardPlugin).GetMethod("LockerPrefix", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo lockerPostfix = typeof(DoorKeycardPlugin).GetMethod("LockerPostfix", BindingFlags.Static | BindingFlags.NonPublic);

                if (doorMethod != null)
                {
                    _harmony.Patch(doorMethod, new HarmonyMethod(doorPrefix), new HarmonyMethod(doorPostfix));
                    Write("patched DoorPermissions.CheckPermissions");
                }

                if (lockerMethod != null)
                {
                    _harmony.Patch(lockerMethod, new HarmonyMethod(lockerPrefix), new HarmonyMethod(lockerPostfix));
                    Write("patched Locker.CheckPerms (scientist/zonemanager/guard blocked for SCP lockers)");
                }

                Write("inventory keycards enabled (doors + lockers)");
            }
            catch (Exception exception)
            {
                Write("patch failed: " + exception.GetType().Name + ": " + exception.Message);
            }

            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            _active = false;
            try { if (_harmony != null) _harmony.UnpatchAll(_harmonyId); } catch { }
            _harmony = null;
            base.OnDisabled();
        }

        private static bool DoorPrefix(DoorPermissions __instance, ReferenceHub __1, ref bool __result)
        {
            if (!_active || __instance == null) return true;
            if (IsScp(__1)) return true;

            KeycardPermissions required = __instance.RequiredPermissions;
            if (required == 0) return true;

            KeycardItem card = FindCard(__1, required, __instance.RequireAll, null);
            if (card == null) return true;

            Allow("door", card, ref __result);
            return false;
        }

        private static void DoorPostfix(DoorPermissions __instance, ReferenceHub __1, ref bool __result)
        {
            if (!_active || __result || __instance == null) return;
            if (IsScp(__1)) return;

            KeycardPermissions required = __instance.RequiredPermissions;
            if (required == 0) return;

            KeycardItem card = FindCard(__1, required, __instance.RequireAll, null);
            if (card == null) return;

            Allow("door", card, ref __result);
        }

        private static bool LockerPrefix(Locker __instance, KeycardPermissions __0, ReferenceHub __1, ref bool __result)
        {
            if (!_active || __1 == null) return true;
            if (IsScp(__1)) return true;
            if (__0 == 0) return true;

            bool scpLocker = IsScpItemLocker(__instance);

            // SCP 物品柜只允许高级卡打开；科学家/区域总监/警卫卡不放行。
            KeycardItem card = FindCard(__1, __0, false, scpLocker ? ScpLockerBlockedCards : null);
            if (card == null)
            {
                if (scpLocker)
                {
                    KeycardItem blocked = FindCard(__1, __0, false, null);
                    if (blocked != null) Write("card " + blocked.ItemTypeId + " blocked from SCP locker");
                }
                return true;
            }

            Allow("locker", card, ref __result);
            return false;
        }

        private static void LockerPostfix(Locker __instance, KeycardPermissions __0, ReferenceHub __1, ref bool __result)
        {
            if (!_active || __result || __1 == null) return;
            if (IsScp(__1)) return;
            if (__0 == 0) return;

            bool scpLocker = IsScpItemLocker(__instance);

            KeycardItem card = FindCard(__1, __0, false, scpLocker ? ScpLockerBlockedCards : null);
            if (card == null) return;

            Allow("locker", card, ref __result);
        }

        private static void Allow(string kind, KeycardItem card, ref bool __result)
        {
            __result = true;
            _opens++;

            if (_opens <= 20)
            {
                Write(kind + " opened with inventory card: " + card.ItemTypeId);
            }
        }

        private static KeycardItem FindCard(ReferenceHub hub, KeycardPermissions required, bool requireAll, System.Collections.Generic.HashSet<ItemType> blocked)
        {
            if (hub == null) return null;
            if (hub.inventory == null || hub.inventory.UserInventory == null) return null;

            System.Collections.Generic.Dictionary<ushort, ItemBase> items = hub.inventory.UserInventory.Items;
            if (items == null) return null;

            foreach (ItemBase item in items.Values)
            {
                KeycardItem card = item as KeycardItem;
                if (card == null) continue;

                if (blocked != null && blocked.Contains(card.ItemTypeId)) continue;

                bool match = requireAll
                    ? (card.Permissions & required) == required
                    : (card.Permissions & required) != 0;

                if (match) return card;
            }

            return null;
        }

        private static bool IsScp(ReferenceHub hub)
        {
            if (hub == null) return false;

            try
            {
                return hub.roleManager != null && hub.roleManager.CurrentRole != null && hub.roleManager.CurrentRole.Team == Team.SCPs;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsScpItemLocker(Locker locker)
        {
            if (locker == null) return false;

            try
            {
                LockerLoot[] loot = locker.Loot;
                if (loot == null) return false;

                for (int i = 0; i < loot.Length; i++)
                {
                    if (loot[i] == null) continue;
                    if (IsScpItem(loot[i].TargetItem)) return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsScpItem(ItemType type)
        {
            return type == ItemType.SCP500
                || type == ItemType.SCP207
                || type == ItemType.SCP018
                || type == ItemType.SCP268
                || type == ItemType.SCP330
                || type == ItemType.SCP2176
                || type == ItemType.SCP244a
                || type == ItemType.SCP244b
                || type == ItemType.SCP1853
                || type == ItemType.SCP1576;
        }

        private static void Write(string message)
        {
            try
            {
                string directory = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.AppendAllText(_logPath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
