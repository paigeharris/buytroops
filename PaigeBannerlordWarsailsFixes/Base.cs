using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace PaigeBannerlordWarsailsFixes
{
    public class SubModule : MBSubModuleBase
    {
        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            try
            {
                if (!(game.GameType is Campaign))
                    return;

                CampaignGameStarter starter = gameStarterObject as CampaignGameStarter;
                if (starter == null)
                    return;

                starter.AddModel(new SafeHeroCreationModel());
                starter.AddBehavior(new WarSailsFixBehavior());
            }
            catch (Exception e)
            {
                SafeNotify("Startup failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void SafeNotify(string message)
        {
            try
            {
                InformationManager.DisplayMessage(
                    new InformationMessage("[PaigeFixes] " + (message ?? string.Empty))
                );
            }
            catch
            {
                // never crash for notifications
            }
        }
    }

    public class SafeHeroCreationModel : DefaultHeroCreationModel
    {
        public override Equipment GetCivilianEquipment(Hero hero)
        {
            try
            {
                if (hero == null)
                    return new Equipment(Equipment.EquipmentType.Civilian);

                if (hero.Mother == null)
                    return EnsureCivilianFallback(hero.CivilianEquipment, hero.BattleEquipment);

                IEnumerable<MBEquipmentRoster> rosters = null;
                try
                {
                    var model = Campaign.Current != null ? Campaign.Current.Models.EquipmentSelectionModel : null;
                    if (model != null)
                        rosters = model.GetEquipmentRostersForDeliveredOffspring(hero);
                }
                catch
                {
                    // keep fallback path alive
                }

                if (rosters != null)
                {
                    foreach (MBEquipmentRoster roster in rosters)
                    {
                        Equipment civilian = TryGetCivilianFromRoster(roster);
                        if (civilian != null)
                            return civilian;
                    }
                }

                return EnsureCivilianFallback(hero.CivilianEquipment, hero.BattleEquipment);
            }
            catch
            {
                return new Equipment(Equipment.EquipmentType.Civilian);
            }
        }

        private static Equipment TryGetCivilianFromRoster(MBEquipmentRoster roster)
        {
            if (roster == null)
                return null;

            try
            {
                MBReadOnlyList<Equipment> all = roster.AllEquipments;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        Equipment equipment = all[i];
                        if (equipment != null && equipment.IsCivilian)
                            return equipment;
                    }
                }

                Equipment fallback = roster.DefaultEquipment;
                if (fallback != null)
                {
                    Equipment converted = new Equipment(Equipment.EquipmentType.Civilian);
                    converted.FillFrom(fallback, false);
                    return converted;
                }
            }
            catch
            {
                // ignore and let caller fallback
            }

            return null;
        }

        private static Equipment EnsureCivilianFallback(Equipment civilian, Equipment battle)
        {
            try
            {
                if (civilian != null)
                    return civilian;

                if (battle != null)
                {
                    Equipment converted = new Equipment(Equipment.EquipmentType.Civilian);
                    converted.FillFrom(battle, false);
                    return converted;
                }
            }
            catch
            {
                // ignore and use hard fallback
            }

            return new Equipment(Equipment.EquipmentType.Civilian);
        }
    }

    public class WarSailsFixBehavior : CampaignBehaviorBase
    {
        private const string SiegeStrategiesMenuId = "menu_siege_strategies";
        private const string LiftBlockadeText = "Lift the blockade";
        private const string DefendBlockadeText = "Defend the blockade";
        private const string LiftBlockadeWarningSuffix = " [Known Native crash risk]";
        private const string LiftBlockadeWarningTooltipText =
            "{=PaigeLiftBlockadeWarning}Warning: Bannerlord Native/War Sails blockade action can throw NullReferenceException (GameMenuItemVM.ExecuteAction) for independent, no-ship rulers in port-siege blockade defense.";
        private const string SafetyLogName = "PaigeFixes.log";

        private readonly HashSet<GameMenuOption> _patchedOptions = new HashSet<GameMenuOption>();
        private readonly Dictionary<GameMenuOption, string> _liftOptionOriginalTexts =
            new Dictionary<GameMenuOption, string>();
        private bool _liftWarningAcknowledged;

        private static readonly FieldInfo EquipmentRosterEquipmentsField =
            typeof(MBEquipmentRoster).GetField("_equipments", BindingFlags.Instance | BindingFlags.NonPublic);

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(
                this, new Action<CampaignGameStarter>(OnSessionLaunchedSafe)
            );
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(
                this, new Action(OnDailyTickSafe)
            );
            CampaignEvents.GameMenuOpened.AddNonSerializedListener(
                this, new Action<MenuCallbackArgs>(OnMenuOpenedSafe)
            );
        }

        public override void SyncData(IDataStore dataStore)
        {
            // no persistent data
        }

        private void OnSessionLaunchedSafe(CampaignGameStarter starter)
        {
            try
            {
                SanitizeEquipmentRostersSafe("session launch");
            }
            catch (Exception e)
            {
                AppendSafetyLog("OnSessionLaunchedSafe failed: " + e);
            }
        }

        private void OnDailyTickSafe()
        {
            try
            {
                SanitizeEquipmentRostersSafe("daily tick");
            }
            catch (Exception e)
            {
                AppendSafetyLog("OnDailyTickSafe failed: " + e);
            }
        }

        private void OnMenuOpenedSafe(MenuCallbackArgs args)
        {
            try
            {
                string menuId = GetMenuIdSafe(args);
                GameMenu gameMenu = args != null && args.MenuContext != null
                    ? args.MenuContext.GameMenu
                    : null;
                if (gameMenu == null)
                    return;

                bool inBlockadeBattle = IsInBlockadeBattle(args);
                bool relevantMenu =
                    string.Equals(menuId, SiegeStrategiesMenuId, StringComparison.OrdinalIgnoreCase) ||
                    (menuId != null && menuId.IndexOf("blockade", StringComparison.OrdinalIgnoreCase) >= 0);

                if (!inBlockadeBattle && !relevantMenu)
                    return;

                foreach (GameMenuOption option in gameMenu.MenuOptions)
                {
                    if (option == null || _patchedOptions.Contains(option))
                        continue;

                    bool isLiftOption = IsLiftBlockadeOption(option);
                    bool isBlockadeOption = IsBlockadeRelatedOption(option);

                    if (!inBlockadeBattle && !isBlockadeOption)
                        continue;

                    if (isLiftOption)
                    {
                        RememberLiftOriginalText(option);
                        if (_liftWarningAcknowledged)
                            RestoreLiftOptionText(option);
                        else
                            TryMarkLiftOptionAsWarning(option);
                    }

                    PatchBlockadeOption(option, isLiftOption);
                    _patchedOptions.Add(option);
                    AppendSafetyLog(
                        "Patched blockade option. menu=" + (menuId ?? "(null)") +
                        ", optionId=" + (option.IdString ?? "(null)") +
                        ", text=" + GetOptionTextSafe(option) +
                        ", isLift=" + isLiftOption +
                        ", inBlockadeBattle=" + inBlockadeBattle
                    );
                }
            }
            catch (Exception e)
            {
                AppendSafetyLog("OnMenuOpenedSafe failed: " + e);
            }
        }

        private void PatchBlockadeOption(GameMenuOption option, bool isLiftOption)
        {
            GameMenuOption.OnConditionDelegate originalCondition = option.OnCondition;
            GameMenuOption.OnConsequenceDelegate originalConsequence = option.OnConsequence;
            string optionLabel = (option.IdString ?? "(null)") + "|" + GetOptionTextSafe(option);

            option.OnCondition = delegate (MenuCallbackArgs args)
            {
                try
                {
                    bool visible = true;
                    if (originalCondition != null)
                        visible = originalCondition(args);

                    if (!visible)
                        return false;

                    if (isLiftOption && ShouldBlockLiftBlockade(args))
                    {
                        if (args != null)
                        {
                            args.IsEnabled = true;
                            if (_liftWarningAcknowledged)
                                args.Tooltip = new TextObject(string.Empty);
                            else
                                args.Tooltip = new TextObject(LiftBlockadeWarningTooltipText);
                        }
                    }

                    return true;
                }
                catch (Exception e)
                {
                    AppendSafetyLog("Blockade option condition wrapper failed (" + optionLabel + "): " + e);
                    return false;
                }
            };

            option.OnConsequence = delegate (MenuCallbackArgs args)
            {
                if (isLiftOption && ShouldBlockLiftBlockade(args))
                {
                    if (!_liftWarningAcknowledged)
                    {
                        _liftWarningAcknowledged = true;
                        RestoreLiftOptionText(option);
                        AppendSafetyLog("Lift-blockade warning acknowledged by player click; restored original button label.");
                    }

                    AppendSafetyLog(
                        "Executing warned lift-blockade consequence (native path may crash): " + optionLabel
                    );
                }

                if (originalConsequence != null)
                    originalConsequence(args);
            };
        }

        private bool ShouldBlockLiftBlockade(MenuCallbackArgs args)
        {
            try
            {
                if (!IsInBlockadeBattle(args))
                    return false;

                Clan playerClan = Clan.PlayerClan;
                bool isIndependent = playerClan == null || playerClan.Kingdom == null;
                bool hasNoShips = HasNoShipsSafe();
                bool isPortSiege = IsPortSiegeContextSafe();

                // Narrow warning to the exact repro profile:
                // independent ruler + no ships + port-city siege blockade context.
                return isIndependent && hasNoShips && isPortSiege;
            }
            catch (Exception e)
            {
                AppendSafetyLog("ShouldBlockLiftBlockade failed; not warning by default: " + e.Message);
                return false;
            }
        }

        private static bool IsLiftBlockadeOption(GameMenuOption option)
        {
            if (option == null)
                return false;

            string id = option.IdString ?? string.Empty;
            if (id.IndexOf("lift", StringComparison.OrdinalIgnoreCase) >= 0 &&
                id.IndexOf("blockade", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            string text = GetOptionTextSafe(option);
            if (text.IndexOf(LiftBlockadeText, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static bool IsBlockadeRelatedOption(GameMenuOption option)
        {
            if (option == null)
                return false;

            string id = option.IdString ?? string.Empty;
            if (id.IndexOf("blockade", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            string text = GetOptionTextSafe(option);
            if (text.IndexOf("blockade", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (text.IndexOf(DefendBlockadeText, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static string GetOptionTextSafe(GameMenuOption option)
        {
            try
            {
                return option != null && option.Text != null
                    ? option.Text.ToString()
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void TryMarkLiftOptionAsWarning(GameMenuOption option)
        {
            try
            {
                string baseText = GetLiftOriginalText(option);
                if (string.IsNullOrWhiteSpace(baseText))
                    return;

                string currentText = GetOptionTextSafe(option);
                if (currentText.IndexOf(LiftBlockadeWarningSuffix, StringComparison.OrdinalIgnoreCase) >= 0)
                    return;

                string warnedText = baseText + LiftBlockadeWarningSuffix;
                if (TrySetOptionTextSafe(option, warnedText))
                    AppendSafetyLog("Updated lift-blockade label with warning text.");
            }
            catch (Exception e)
            {
                AppendSafetyLog("TryMarkLiftOptionAsWarning failed: " + e.Message);
            }
        }

        private void RememberLiftOriginalText(GameMenuOption option)
        {
            try
            {
                if (option == null || _liftOptionOriginalTexts.ContainsKey(option))
                    return;

                string currentText = GetOptionTextSafe(option);
                if (string.IsNullOrWhiteSpace(currentText))
                    return;

                string normalized = currentText;
                int suffixIndex = normalized.IndexOf(LiftBlockadeWarningSuffix, StringComparison.OrdinalIgnoreCase);
                if (suffixIndex >= 0)
                    normalized = normalized.Substring(0, suffixIndex);

                normalized = normalized.TrimEnd();
                if (!string.IsNullOrWhiteSpace(normalized))
                    _liftOptionOriginalTexts[option] = normalized;
            }
            catch
            {
                // ignore option text memory failures
            }
        }

        private string GetLiftOriginalText(GameMenuOption option)
        {
            string value;
            if (option != null && _liftOptionOriginalTexts.TryGetValue(option, out value))
                return value;

            return GetOptionTextSafe(option);
        }

        private void RestoreLiftOptionText(GameMenuOption option)
        {
            try
            {
                string baseText = GetLiftOriginalText(option);
                if (string.IsNullOrWhiteSpace(baseText))
                    return;

                string currentText = GetOptionTextSafe(option);
                if (string.Equals(currentText, baseText, StringComparison.Ordinal))
                    return;

                if (TrySetOptionTextSafe(option, baseText))
                    AppendSafetyLog("Restored original lift-blockade button text after warning acknowledgement.");
            }
            catch (Exception e)
            {
                AppendSafetyLog("RestoreLiftOptionText failed: " + e.Message);
            }
        }

        private static bool TrySetOptionTextSafe(GameMenuOption option, string text)
        {
            try
            {
                if (option == null || string.IsNullOrWhiteSpace(text))
                    return false;

                Type type = option.GetType();

                PropertyInfo textProperty = type.GetProperty("Text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (textProperty != null && textProperty.CanWrite)
                {
                    if (typeof(TextObject).IsAssignableFrom(textProperty.PropertyType))
                    {
                        textProperty.SetValue(option, new TextObject(text), null);
                        return true;
                    }

                    if (typeof(string).IsAssignableFrom(textProperty.PropertyType))
                    {
                        textProperty.SetValue(option, text, null);
                        return true;
                    }
                }

                FieldInfo textField =
                    type.GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic) ??
                    type.GetField("Text", BindingFlags.Instance | BindingFlags.NonPublic) ??
                    type.GetField("text", BindingFlags.Instance | BindingFlags.NonPublic);

                if (textField != null)
                {
                    if (typeof(TextObject).IsAssignableFrom(textField.FieldType))
                    {
                        textField.SetValue(option, new TextObject(text));
                        return true;
                    }

                    if (typeof(string).IsAssignableFrom(textField.FieldType))
                    {
                        textField.SetValue(option, text);
                        return true;
                    }
                }
            }
            catch
            {
                // ignore text mutation failures
            }

            return false;
        }

        private static bool IsInBlockadeBattle(MenuCallbackArgs args)
        {
            try
            {
                if (PlayerEncounter.IsActive)
                {
                    MapEvent battle = PlayerEncounter.Battle;
                    if (battle != null)
                    {
                        string eventType = battle.EventType.ToString();
                        if (!string.IsNullOrEmpty(eventType) &&
                            eventType.IndexOf("blockade", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }

                string menuId = GetMenuIdSafe(args);
                if (!string.IsNullOrEmpty(menuId) &&
                    menuId.IndexOf("blockade", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch
            {
                // ignore and report false
            }

            return false;
        }

        private static bool HasNoShipsSafe()
        {
            try
            {
                MobileParty mainParty = MobileParty.MainParty;
                if (mainParty == null)
                    return false;

                int shipCount;
                if (TryGetIntMemberSafe(mainParty, out shipCount, "ShipCount", "TotalShipCount", "NumberOfShips", "TotalShips"))
                    return shipCount <= 0;

                PartyBase partyBase = mainParty.Party;
                if (TryGetIntMemberSafe(partyBase, out shipCount, "ShipCount", "TotalShipCount", "NumberOfShips", "TotalShips"))
                    return shipCount <= 0;
            }
            catch
            {
                // ignore and fail open
            }

            return false;
        }

        private static bool IsPortSiegeContextSafe()
        {
            try
            {
                Settlement besieged = GetPlayerBesiegedSettlementSafe();
                if (besieged != null)
                    return IsPortSettlementSafe(besieged);

                Settlement current = Settlement.CurrentSettlement;
                if (current != null)
                    return IsPortSettlementSafe(current);
            }
            catch
            {
                // ignore and fail closed
            }

            return false;
        }

        private static Settlement GetPlayerBesiegedSettlementSafe()
        {
            try
            {
                Type playerSiegeType =
                    Type.GetType("TaleWorlds.CampaignSystem.Siege.PlayerSiege, TaleWorlds.CampaignSystem") ??
                    Type.GetType("TaleWorlds.CampaignSystem.PlayerSiege, TaleWorlds.CampaignSystem");
                if (playerSiegeType == null)
                    return null;

                PropertyInfo playerSiegeEventProperty =
                    playerSiegeType.GetProperty("PlayerSiegeEvent", BindingFlags.Public | BindingFlags.Static);
                if (playerSiegeEventProperty == null)
                    return null;

                object siegeEvent = playerSiegeEventProperty.GetValue(null, null);
                if (siegeEvent == null)
                    return null;

                PropertyInfo besiegedSettlementProperty =
                    siegeEvent.GetType().GetProperty("BesiegedSettlement", BindingFlags.Public | BindingFlags.Instance);
                if (besiegedSettlementProperty == null)
                    return null;

                return besiegedSettlementProperty.GetValue(siegeEvent, null) as Settlement;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPortSettlementSafe(Settlement settlement)
        {
            if (settlement == null)
                return false;

            try
            {
                bool isTown;
                if (!TryGetBoolMemberSafe(settlement, out isTown, "IsTown") || !isTown)
                    return false;

                bool isPort;
                if (TryGetBoolMemberSafe(settlement, out isPort, "IsPort"))
                    return isPort;

                if (settlement.Town != null && TryGetBoolMemberSafe(settlement.Town, out isPort, "IsPort"))
                    return isPort;
            }
            catch
            {
                // ignore and fail closed
            }

            return false;
        }

        private static bool TryGetIntMemberSafe(object instance, out int value, params string[] names)
        {
            value = 0;
            if (instance == null || names == null)
                return false;

            Type t = instance.GetType();

            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                PropertyInfo property = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property != null)
                {
                    if (TryConvertToIntSafe(property.GetValue(instance, null), out value))
                        return true;
                }

                FieldInfo field = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    if (TryConvertToIntSafe(field.GetValue(instance), out value))
                        return true;
                }
            }

            return false;
        }

        private static bool TryGetBoolMemberSafe(object instance, out bool value, params string[] names)
        {
            value = false;
            if (instance == null || names == null)
                return false;

            Type t = instance.GetType();

            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                PropertyInfo property = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property != null)
                {
                    if (TryConvertToBoolSafe(property.GetValue(instance, null), out value))
                        return true;
                }

                FieldInfo field = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    if (TryConvertToBoolSafe(field.GetValue(instance), out value))
                        return true;
                }
            }

            return false;
        }

        private static bool TryConvertToBoolSafe(object raw, out bool value)
        {
            value = false;
            if (raw == null)
                return false;

            if (raw is bool)
            {
                value = (bool)raw;
                return true;
            }

            bool parsed;
            if (bool.TryParse(raw.ToString(), out parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }

        private static bool TryConvertToIntSafe(object raw, out int value)
        {
            value = 0;
            if (raw == null)
                return false;

            if (raw is int)
            {
                value = (int)raw;
                return true;
            }

            if (raw is short)
            {
                value = (short)raw;
                return true;
            }

            if (raw is byte)
            {
                value = (byte)raw;
                return true;
            }

            if (raw is long)
            {
                value = (int)(long)raw;
                return true;
            }

            if (raw is float)
            {
                value = (int)(float)raw;
                return true;
            }

            if (raw is double)
            {
                value = (int)(double)raw;
                return true;
            }

            int parsed;
            if (int.TryParse(raw.ToString(), out parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }

        private static string GetMenuIdSafe(MenuCallbackArgs args)
        {
            try
            {
                if (args == null || args.MenuContext == null || args.MenuContext.GameMenu == null)
                    return null;

                return args.MenuContext.GameMenu.StringId;
            }
            catch
            {
                return null;
            }
        }

        private void SanitizeEquipmentRostersSafe(string context)
        {
            try
            {
                if (EquipmentRosterEquipmentsField == null || Campaign.Current == null)
                    return;

                MBReadOnlyList<MBEquipmentRoster> rosters =
                    TaleWorlds.CampaignSystem.Extensions.MBEquipmentRosterExtensions.All;
                if (rosters == null)
                    return;

                int rosterCount = 0;
                int replacedCount = 0;

                foreach (MBEquipmentRoster roster in rosters)
                {
                    if (roster == null)
                        continue;

                    rosterCount++;
                    IList equipments = EquipmentRosterEquipmentsField.GetValue(roster) as IList;
                    if (equipments == null || equipments.Count == 0)
                        continue;

                    Equipment fallback = BuildEquipmentFallbackSafe(roster, equipments);
                    for (int i = 0; i < equipments.Count; i++)
                    {
                        if (equipments[i] == null)
                        {
                            equipments[i] = new Equipment(fallback);
                            replacedCount++;
                        }
                    }
                }

                if (replacedCount > 0)
                {
                    AppendSafetyLog(
                        "Equipment roster cleanup (" + (context ?? "unknown") + "): replaced " +
                        replacedCount + " null entries across " + rosterCount + " rosters."
                    );
                }
            }
            catch (Exception e)
            {
                AppendSafetyLog("Equipment roster cleanup failed: " + e);
            }
        }

        private static Equipment BuildEquipmentFallbackSafe(MBEquipmentRoster roster, IList equipments)
        {
            try
            {
                if (equipments != null)
                {
                    foreach (object value in equipments)
                    {
                        Equipment equipment = value as Equipment;
                        if (equipment != null)
                            return equipment;
                    }
                }

                if (roster != null && roster.DefaultEquipment != null)
                    return roster.DefaultEquipment;
            }
            catch
            {
                // ignore and use hard fallback
            }

            return new Equipment(Equipment.EquipmentType.Civilian);
        }

        private void AppendSafetyLog(string message)
        {
            try
            {
                string dir =
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) +
                    "\\Mount and Blade II Bannerlord\\Configs";
                string path = dir + "\\" + SafetyLogName;

                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                    " [PaigeFixes] " + (message ?? string.Empty) + Environment.NewLine
                );
            }
            catch
            {
                // never crash for logging
            }
        }
    }
}
