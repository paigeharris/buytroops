using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace BuyTroops
{
    public class Base : MBSubModuleBase
    {
        private AddBuyMenu _buyMenuBehavior;

        protected override void OnSubModuleLoad()
        {
            // intentionally empty
            //
            //
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            try
            {
                Campaign campaign = game.GameType as Campaign;
                if (campaign == null) return;

                CampaignGameStarter starter = gameStarterObject as CampaignGameStarter;
                if (starter == null) return;

                starter.AddModel(new BuyTroopsSafeHeroCreationModel());
                _buyMenuBehavior = new AddBuyMenu();
                starter.AddBehavior(_buyMenuBehavior);
            }
            catch (Exception e)
            {
                try
                {
                    InformationManager.DisplayMessage(new InformationMessage("[BuyTroops] Startup failed: " + e.GetType().Name + ": " + e.Message));
                }
                catch
                {
                    // never crash on load
                }
            }
        }
    }

    public class BuyTroopsSafeHeroCreationModel : DefaultHeroCreationModel
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
                    var model = Campaign.Current?.Models?.EquipmentSelectionModel;
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
                // ignore and use empty civilian equipment
            }

            return new Equipment(Equipment.EquipmentType.Civilian);
        }
    }

    public class AddBuyMenu : CampaignBehaviorBase
    {
        private const string ModMenuName = "elite_retinue_mod";
        private const string DefaultFactionKey = "Empire";
        private const string WarSailsModuleId = "WarSails";
        private static readonly bool EnableMenuIdDebug = false;

        private CampaignGameStarter _starter;
        private bool _disabled;
        private string _disabledReason;
        private DateTime _lastDisabledNoticeUtc = DateTime.MinValue;
        private bool _contextPaused;
        private string _contextPauseReason;
        private DateTime _lastContextPauseNoticeUtc = DateTime.MinValue;
        private bool _menuRegistrationComplete;
        private static readonly FieldInfo EquipmentRosterEquipmentsField =
            typeof(MBEquipmentRoster).GetField("_equipments", BindingFlags.Instance | BindingFlags.NonPublic);
        private const int DisableNoticeCooldownSeconds = 10;

        /* ===================== EVENTS ===================== */

        public override void RegisterEvents()
        {
            try
            {
                CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(OnSessionLaunched));
                CampaignEvents.GameMenuOpened.AddNonSerializedListener(this, new Action<MenuCallbackArgs>(OnMenuOpened));
                CampaignEvents.OnPlayerSiegeStartedEvent.AddNonSerializedListener(this, new Action(OnPlayerSiegeStartedSafe));
                CampaignEvents.MapEventStarted.AddNonSerializedListener(this, new Action<MapEvent, PartyBase, PartyBase>(OnMapEventStartedSafe));
                CampaignEvents.MapEventEnded.AddNonSerializedListener(this, new Action<MapEvent>(OnMapEventEndedSafe));
                CampaignEvents.OnSiegeEngineDestroyedEvent.AddNonSerializedListener(this, new Action<MobileParty, Settlement, BattleSideEnum, SiegeEngineType>(OnSiegeEngineDestroyedSafe));
                CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, new Action(OnDailyTickSafe));
            }
            catch (Exception e)
            {
                DisableMod("RegisterEvents failed.", e);
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            // no persistent data

        }

        /* ===================== SAFETY HELPERS ===================== */

        private void SafeLog(string msg)
        {
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(msg ?? ""));
            }
            catch
            {
                // logging should never crash
            }
        }

        private void AppendSafetyLog(string message)
        {
            try
            {
                string dir =
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) +
                    "\\Mount and Blade II Bannerlord\\Configs";
                string path = dir + "\\BuyTroops_Safety.log";

                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                    " [BuyTroops] " + (message ?? "") + Environment.NewLine
                );
            }
            catch
            {
                // never crash for logging
            }
        }

        private void DisableMod(string reason, Exception e = null)
        {
            string cleanReason = string.IsNullOrWhiteSpace(reason)
                ? "Unknown safety shutdown reason."
                : reason.Trim();

            if (!_disabled)
            {
                _disabled = true;
                _disabledReason = cleanReason;
                SafeLog("[BuyTroops] Safety mode enabled. Menus are disabled for this session: " + _disabledReason);
            }
            else if (string.IsNullOrWhiteSpace(_disabledReason))
            {
                _disabledReason = cleanReason;
            }

            AppendSafetyLog("DISABLED: " + cleanReason);
            if (e != null)
                AppendSafetyLog(e.ToString());
        }

        private void NotifyDisabled(string context)
        {
            if (!_disabled) return;

            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                if ((nowUtc - _lastDisabledNoticeUtc).TotalSeconds < DisableNoticeCooldownSeconds)
                    return;

                _lastDisabledNoticeUtc = nowUtc;
                string reason = string.IsNullOrWhiteSpace(_disabledReason) ? "Unknown reason." : _disabledReason;
                SafeLog("[BuyTroops] Blocked (" + (context ?? "unknown") + "): " + reason);
            }
            catch
            {
                // never crash for notifications
                //
            }
        }

        private bool IsDisabledAndNotify(string context)
        {
            if (!_disabled) return false;
            NotifyDisabled(context);
            return true;
        }

        private void SetContextPause(string reason)
        {
            string cleanReason = string.IsNullOrWhiteSpace(reason)
                ? "Unsafe game state."
                : reason.Trim();

            bool changed = !_contextPaused || !string.Equals(_contextPauseReason, cleanReason, StringComparison.Ordinal);

            _contextPaused = true;
            _contextPauseReason = cleanReason;

            if (changed)
                AppendSafetyLog("PAUSED: " + cleanReason);
        }

        private void NotifyContextPaused(string context)
        {
            if (!_contextPaused) return;

            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                if ((nowUtc - _lastContextPauseNoticeUtc).TotalSeconds < DisableNoticeCooldownSeconds)
                    return;

                _lastContextPauseNoticeUtc = nowUtc;
                string reason = string.IsNullOrWhiteSpace(_contextPauseReason) ? "Unsafe game state." : _contextPauseReason;
                SafeLog("[BuyTroops] Temporarily blocked (" + (context ?? "unknown") + "): " + reason);
            }
            catch
            {
                // never crash for notifications
            }
        }

        private void TryResumeFromContextPause(string context)
        {
            if (!_contextPaused) return;

            if (IsSiegeContextUnsafe(null))
            {
                NotifyContextPaused(context);
                return;
            }

            _contextPaused = false;
            _contextPauseReason = null;
            AppendSafetyLog("RESUMED: " + (context ?? "unknown"));
            SafeLog("[BuyTroops] Re-enabled: safe context restored.");
        }

        private bool ShouldBlockMenuAction(MenuCallbackArgs args, string context)
        {
            if (IsDisabledAndNotify(context))
                return true;

            if (IsSiegeContextUnsafe(args))
            {
                SetContextPause("Unsafe combat/siege state detected while " + context + ".");
                NotifyContextPaused(context);
                return true;
            }

            TryResumeFromContextPause(context);
            return false;
        }

        private bool TryAddTroop(string id, int amount)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id)) return false;
                if (amount < 1) amount = 1;

                MobileParty party = MobileParty.MainParty;
                if (party == null) return false;

                CharacterObject character = CharacterObject.Find(id);
                if (character == null)
                {
                    SafeLog("BuyTroops missing troop id: " + id);
                    return false;
                }

                party.AddElementToMemberRoster(character, amount, false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsWarSailsLoadedSafe()
        {
            try
            {
                if (IsModuleLoadedSafe(WarSailsModuleId))
                    return true;

                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        string asmName = asm.GetName().Name ?? "";
                        if (asmName.IndexOf(WarSailsModuleId, StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private bool IsModuleLoadedSafe(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type helper = asm.GetType("TaleWorlds.ModuleManager.ModuleHelper");
                    if (helper == null) continue;

                    string[] methodNames = new string[]
                    {
                        "GetLoadedModules",
                        "GetModules",
                        "GetModuleList",
                        "GetModuleInfoList"
                    };

                    foreach (string methodName in methodNames)
                    {
                        MethodInfo mi = helper.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                        if (mi == null) continue;
                        if (mi.GetParameters().Length != 0) continue;

                        object result = mi.Invoke(null, null);
                        System.Collections.IEnumerable list = result as System.Collections.IEnumerable;
                        if (list == null) continue;

                        foreach (object item in list)
                        {
                            if (MatchesModuleIdSafe(item, id))
                                return true;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private bool MatchesModuleIdSafe(object moduleInfo, string id)
        {
            if (moduleInfo == null || string.IsNullOrWhiteSpace(id)) return false;

            try
            {
                Type t = moduleInfo.GetType();
                string[] propNames = new string[]
                {
                    "Id",
                    "ModuleId",
                    "Name",
                    "ModuleName"
                };

                foreach (string propName in propNames)
                {
                    PropertyInfo pi = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                    if (pi == null) continue;
                    object val = pi.GetValue(moduleInfo, null);
                    string s = val as string;
                    if (!string.IsNullOrEmpty(s) && string.Equals(s, id, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                string fallback = moduleInfo.ToString();
                if (!string.IsNullOrEmpty(fallback) &&
                    fallback.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch
            {
                // ignore
                //
                //
                //
            }

            return false;
        }

        private bool HasPortSafe()
        {
            try
            {
                Settlement s = Settlement.CurrentSettlement;
                if (s == null) return false;

                Town t = s.Town;
                if (t == null) return false;

                Type tt = t.GetType();

                PropertyInfo isPort = tt.GetProperty("IsPort", BindingFlags.Public | BindingFlags.Instance);
                if (isPort != null && isPort.PropertyType == typeof(bool))
                    return (bool)isPort.GetValue(t, null);

                PropertyInfo hasPort = tt.GetProperty("HasPort", BindingFlags.Public | BindingFlags.Instance);
                if (hasPort != null && hasPort.PropertyType == typeof(bool))
                    return (bool)hasPort.GetValue(t, null);

                PropertyInfo port = tt.GetProperty("Port", BindingFlags.Public | BindingFlags.Instance);
                if (port != null)
                {
                    object portObj = port.GetValue(t, null);
                    if (portObj == null) return false;

                    Type pt = portObj.GetType();
                    PropertyInfo isActive = pt.GetProperty("IsActive", BindingFlags.Public | BindingFlags.Instance);
                    if (isActive != null && isActive.PropertyType == typeof(bool))
                        return (bool)isActive.GetValue(portObj, null);

                    PropertyInfo isEnabled = pt.GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Instance);
                    if (isEnabled != null && isEnabled.PropertyType == typeof(bool))
                        return (bool)isEnabled.GetValue(portObj, null);

                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private string GetMenuIdSafe(MenuCallbackArgs args)
        {
            if (args == null) return null;

            try
            {
                object ctx = GetPropertyValueSafe(args, "MenuContext");
                object gm = GetPropertyValueSafe(ctx, "GameMenu");

                string gmId = GetStringPropertySafe(gm, "StringId", "Id", "MenuId", "MenuStringId");
                if (!string.IsNullOrEmpty(gmId))
                    return gmId;

                string ctxId = GetStringPropertySafe(ctx, "StringId", "Id", "MenuId", "MenuStringId");
                if (!string.IsNullOrEmpty(ctxId))
                    return ctxId;

                return GetStringPropertySafe(args, "MenuId", "MenuStringId", "StringId", "Id");
            }
            catch
            {
                return null;
            }
        }

        private bool IsSiegeContextUnsafe(MenuCallbackArgs args)
        {
            try
            {
                MobileParty mainParty = MobileParty.MainParty;
                if (mainParty != null)
                {
                    if (mainParty.SiegeEvent != null)
                        return true;

                    if (mainParty.BesiegedSettlement != null)
                        return true;

                    Settlement partySettlement = mainParty.CurrentSettlement;
                    if (partySettlement != null && partySettlement.IsUnderSiege)
                        return true;
                }

                Settlement currentSettlement = Settlement.CurrentSettlement;
                if (currentSettlement != null && currentSettlement.IsUnderSiege)
                    return true;

                if (PlayerEncounter.IsActive)
                {
                    Settlement encounterSettlement = PlayerEncounter.EncounterSettlement;
                    if (encounterSettlement != null && encounterSettlement.IsUnderSiege)
                        return true;

                    MapEvent battle = PlayerEncounter.Battle;
                    if (battle != null && (battle.IsSiegeAssault || battle.IsSiegeOutside || battle.IsSiegeAmbush))
                        return true;
                }

                string menuId = GetMenuIdSafe(args);
                if (!string.IsNullOrEmpty(menuId) &&
                    menuId.IndexOf("siege", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch (Exception e)
            {
                DisableMod("Siege/context safety check failed.", e);
                return true;
            }

            return false;
        }

        private string GetCultureKeySafe()
        {
            string fallback = DefaultFactionKey;

            try
            {
                string rawId = null;
                string source = null;

                // 1) Settlement culture (only valid when actually in a settlement context)
                Settlement s = Settlement.CurrentSettlement;
                if (s != null && s.Culture != null && !string.IsNullOrEmpty(s.Culture.StringId))
                {
                    rawId = s.Culture.StringId;
                    source = "Settlement.CurrentSettlement";
                }

                // 2) Fallback: hero culture
                if (string.IsNullOrEmpty(rawId))
                {
                    Hero h = Hero.MainHero;
                    if (h != null && h.Culture != null && !string.IsNullOrEmpty(h.Culture.StringId))
                    {
                        rawId = h.Culture.StringId;
                        source = "Hero.MainHero";
                    }
                }

                if (string.IsNullOrEmpty(rawId))
                {
                    //SafeLog("[BuyTroops] Culture rawId missing. Fallback -> " + fallback);
                    return fallback;
                }

                string id = rawId.Trim().ToLowerInvariant();

                //SafeLog("[BuyTroops] Culture rawId='" + rawId + "' normalized='" + id + "' source=" + source);

                switch (id)
                {
                    case "empire": return "Empire";
                    case "sturgia": return "Sturgia";
                    case "battania": return "Battania";
                    case "vlandia": return "Vlandia";
                    case "aserai": return "Aserai";
                    case "khuzait": return "Khuzait";

                    // your custom culture id for nords might NOT be "nords"
                    case "nords": return "Nords";
                    case "nord": return "Nords";
                    case "nordic": return "Nords";

                    default:
                        //SafeLog("[BuyTroops] Unknown culture id='" + id + "'. Fallback -> " + fallback);
                        return fallback;
                }
            }
            catch (Exception e)
            {
                //SafeLog("[BuyTroops] Culture exception: " + e.Message + ". Fallback -> " + fallback);
                return fallback;
            }
        }




        private FactionTroops GetTroopsSafe(string key)
        {
            try
            {
                FactionTroops t;
                if (!string.IsNullOrEmpty(key) && factions.TryGetValue(key, out t) && t != null)
                    return t;

                if (factions.TryGetValue(DefaultFactionKey, out t) && t != null)
                    return t;

                // absolute last resort fallback (should never be needed)
                return new FactionTroops(
                    "imperial_legionary",
                    "imperial_palatine_guard",
                    "imperial_elite_cataphract",
                    "bucellarii",
                    "imperial_legionary",
                    "imperial_palatine_guard",
                    "imperial_elite_cataphract"
                );
            }
            catch
            {
                return new FactionTroops(
                    "imperial_legionary",
                    "imperial_palatine_guard",
                    "imperial_elite_cataphract",
                    "bucellarii",
                    "imperial_legionary",
                    "imperial_palatine_guard",
                    "imperial_elite_cataphract"
                );
            }
        }

        /* ===================== DATA ===================== */

        public class FactionTroops
        {
            public string infantry;
            public string archers;
            public string cavalry;
            public string horseArchers;
            public string wildcard1;
            public string wildcard2;
            public string wildcard3;

            public FactionTroops(string i, string a, string c, string h, string w1, string w2, string w3)
            {
                infantry = i;
                archers = a;
                cavalry = c;
                horseArchers = h;
                wildcard1 = w1;
                wildcard2 = w2;
                wildcard3 = w3;
            }

            public FactionTroops(string i, string a, string c, string h, string w1)
            {
                infantry = i;
                archers = a;
                cavalry = c;
                horseArchers = h;
                wildcard1 = w1;
                wildcard2 = w1;
                wildcard3 = w1;
            }

            public FactionTroops(string i, string a, string c, string h)
            {
                infantry = i;
                archers = a;
                cavalry = c;
                horseArchers = h;
                wildcard1 = a;
                wildcard2 = h;
                wildcard3 = i;
            }

            public FactionTroops(string i, string a, string c, string h, string w1, string w2)
            {
                infantry = i;
                archers = a;
                cavalry = c;
                horseArchers = h;
                wildcard1 = w1;
                wildcard2 = w2;
                wildcard3 = w2;
            }
        }

        public static readonly Dictionary<string, FactionTroops> factions = new Dictionary<string, FactionTroops>()
        {
            { "Empire", new FactionTroops("imperial_legionary", "imperial_palatine_guard", "imperial_elite_cataphract", "bucellarii", "legion_of_the_betrayed_tier_3", "imperial_sergeant_crossbowman", "imperial_elite_menavliaton") },
            { "Sturgia", new FactionTroops("sturgian_shock_troop", "sturgian_veteran_bowman", "druzhinnik_champion", "sturgian_horse_raider", "skolderbrotva_tier_3", "sturgian_ulfhednar") },
            { "Battania", new FactionTroops("battanian_veteran_falxman", "battanian_fian_champion", "battanian_horseman", "battanian_mounted_skirmisher", "battanian_oathsworn", "battanian_fian_champion") },
            { "Vlandia", new FactionTroops("vlandian_sergeant", "vlandian_sharpshooter", "vlandian_banner_knight", "vlandian_vanguard", "vlandian_voulgier", "vlandian_pikeman") },
            { "Aserai", new FactionTroops("aserai_veteran_infantry", "aserai_master_archer", "aserai_vanguard_faris", "aserai_mameluke_heavy_cavalry", "ghilman_tier_3", "mamluke_palace_guard", "beni_zilal_tier_3") },
            { "Khuzait", new FactionTroops("khuzait_darkhan", "khuzait_marksman", "khuzait_heavy_lancer", "khuzait_khans_guard", "karakhuzaits_tier_3", "khuzait_marksman") },
            { "Bandits", new FactionTroops("sea_raiders_raider", "forest_bandits_raider", "desert_bandits_raider", "steppe_bandits_chief", "mountain_bandits_raider") },


{ "Nords", new FactionTroops(
    "nord_huscarl",                 // infantry (elite)
    "nord_marksman",                // archers (elite)
    "veteran_caravan_guard_nord",   // cavalry (ACTUALLY MOUNTED)
    "nord_spear_warrior", // cav / raider
    "nord_berserkr",                // wildcard1 (shock)
    "nord_ulfhednar",               // wildcard2 (elite shock)
    "nord_skathi"                 // wildcard3 (elite guard)
) },
        };

        /* ===================== RETINUES ===================== */

        private void AddEliteRetinueSafe(FactionTroops t)
        {
            if (t == null) t = GetTroopsSafe(DefaultFactionKey);

            TryAddTroop(t.horseArchers, 15);
            TryAddTroop(t.cavalry, 10);
            TryAddTroop(t.archers, 20);
            TryAddTroop(t.infantry, 15);
            TryAddTroop(t.wildcard1, 10);
            TryAddTroop(t.wildcard2, 5);
            TryAddTroop(t.wildcard3, 5);
        }

        private void AddBasicRetinueSafe(FactionTroops t)
        {
            if (t == null) t = GetTroopsSafe(DefaultFactionKey);

            TryAddTroop(t.horseArchers, 10);
            TryAddTroop(t.cavalry, 10);
            TryAddTroop(t.archers, 15);
            TryAddTroop(t.infantry, 10);
            TryAddTroop(t.wildcard1, 5);
        }

        private void AddBanditRetinueSafe(FactionTroops t)
        {
            if (t == null) t = GetTroopsSafe("Bandits");

            TryAddTroop(t.horseArchers, 5);
            TryAddTroop(t.cavalry, 5);
            TryAddTroop(t.archers, 10);
            TryAddTroop(t.infantry, 5);
            TryAddTroop(t.wildcard1, 5);
        }

        private void AddPirateRetinueSafe()
        {
            try
            {
                string cultureId = GetCultureIdSafe();
                string pirateId = GetPirateIdForCulture(cultureId);
                if (string.IsNullOrEmpty(pirateId))
                    return;

                TryAddTroop(pirateId, 16);
            }
            catch (Exception e)
            {
                DisableMod("AddPirateRetinueSafe failed.", e);
            }
        }

        private string GetCultureIdSafe()
        {
            try
            {
                Settlement s = Settlement.CurrentSettlement;
                if (s != null && s.Culture != null && !string.IsNullOrEmpty(s.Culture.StringId))
                    return s.Culture.StringId;

                Hero h = Hero.MainHero;
                if (h != null && h.Culture != null && !string.IsNullOrEmpty(h.Culture.StringId))
                    return h.Culture.StringId;
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private bool HasPirateForCurrentPortCultureSafe()
        {
            try
            {
                string cultureId = GetCultureIdSafe();
                if (string.IsNullOrEmpty(cultureId)) return false;

                string pirateId = GetPirateIdForCulture(cultureId);
                if (string.IsNullOrEmpty(pirateId)) return false;

                return CharacterObject.Find(pirateId) != null;
            }
            catch
            {
                return false;
            }
        }

        private string GetPirateIdForCulture(string cultureId)
        {
            try
            {
                if (string.IsNullOrEmpty(cultureId)) return null;

                string id = cultureId.Trim().ToLowerInvariant();

                switch (id)
                {
                    case "vlandia": return "vlandian_marine_t5";
                    case "aserai": return "aserai_marine_t5";
                    case "empire": return "empire_marine_t5";
                    case "battania": return "battanian_marine_t5";
                    case "sturgia": return "sturgian_marine_t5";
                    case "khuzait": return "khuzait_marine_t5";
                    case "nords": return "nord_marine_t5";
                    case "nord": return "nord_marine_t5";
                    case "nordic": return "nord_marine_t5";
                    default: return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private void AddRetinue(string type)
        {
            try
            {
                string cultureKey = GetCultureKeySafe();
                //SafeLog("Culture: " + cultureKey);

                FactionTroops troops = GetTroopsSafe(cultureKey);

                if (type == "elite")
                {
                    AddEliteRetinueSafe(troops);
                }
                else if (type == "basic")
                {
                    AddBasicRetinueSafe(troops);
                }
                else if (type == "bandit")
                {
                    AddBanditRetinueSafe(GetTroopsSafe("Bandits"));
                }
                else if (type == "pirate")
                {
                    AddPirateRetinueSafe();
                }
                else if (type == "fian")
                {
                    if (cultureKey == "Nords")
                    {
                        TryAddTroop("nord_berserkr", 1);
                    }
                    else if (cultureKey == "Vlandia")
                    {
                        TryAddTroop("vlandian_banner_knight", 1);
                    }
                    else if (cultureKey == "Khuzait")
                    {
                        TryAddTroop("khuzait_khans_guard", 1);
                    }
                    else if (cultureKey == "Sturgia")
                    {
                        TryAddTroop("druzhinnik_champion", 1);
                    }
                    else if (cultureKey == "Aserai")
                    {
                        TryAddTroop("ghilman_tier_3", 1);
                    }
                    else if (cultureKey == "Empire")
                    {
                        TryAddTroop("imperial_elite_cataphract", 1);
                    }
                    else
                    {
                        TryAddTroop("battanian_fian_champion", 1);
                    }
                }
                else if (type == "sisters")
                {
                    TryAddTroop("sword_sisters_sister_t5", 25);
                    TryAddTroop("sword_sisters_sister_infantry_t5", 25);
                }
                else
                {
                    TryAddTroop("imperial_legionary", 1);
                }
            }
            catch (Exception e)
            {
                DisableMod("AddRetinue failed for type '" + (type ?? "(null)") + "'.", e);
            }
        }

        private void PurchaseRetinue(string type, int cost)
        {
            try
            {
                Hero hero = Hero.MainHero;
                if (hero == null)
                {
                    SafeLog("No main hero found.");
                    return;
                }

                if (cost < 0) cost = 0;

                if (hero.Gold >= cost)
                {
                    string label = type;
                    if (type == "fian") label = "savage";
                    else if (type == "elite") label = "elite";
                    else if (type == "basic") label = "basic";
                    else if (type == "bandit") label = "bandit";
                    else if (type == "sisters") label = "sisters";
                    else if (type == "pirate") label = "pirate";

                    SafeLog("Recruiting " + label + " retinue for " + cost + " denars.");
                    hero.ChangeHeroGold(-cost);
                    AddRetinue(type);
                }
                else
                {
                    string label = type;
                    if (type == "fian") label = "savage";
                    else if (type == "elite") label = "elite";
                    else if (type == "basic") label = "basic";
                    else if (type == "bandit") label = "bandit";
                    else if (type == "sisters") label = "sisters";
                    else if (type == "pirate") label = "pirate";

                    SafeLog("Not enough denars. " + cost + " required to recruit " + label + " retinue.");
                }
            }
            catch (Exception e)
            {
                DisableMod("PurchaseRetinue failed for type '" + (type ?? "(null)") + "'.", e);
            }
        }

        /* ===================== MENUS ===================== */

        private void LogToFile(string message)
        {
            try
            {
                string dir =
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) +
                    "\\Mount and Blade II Bannerlord";

                string path = dir + "\\BuyTroops_NordDump.txt";

                System.IO.Directory.CreateDirectory(dir);

                System.IO.File.AppendAllText(
                    path,
                    DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine
                );
            }
            catch
            {
                // never crash for logging
                //
            }
        }

        private void DumpNordTroopIdsToFile()
        {
            try
            {
                LogToFile("===== NORD TROOP DUMP START =====");

                foreach (CharacterObject c in CharacterObject.All)
                {
                    if (c != null && !string.IsNullOrEmpty(c.StringId))
                    {
                        if (c.StringId.ToLower().Contains("nord"))
                        {
                            LogToFile(c.StringId);
                        }
                    }
                }

                LogToFile("===== NORD TROOP DUMP END =====");
            }
            catch (Exception e)
            {
                LogToFile("ERROR dumping nord troops: " + e.Message);
            }
        }


        private void OnSessionLaunched(CampaignGameStarter starter)
        {

            try
            {
                if (IsDisabledAndNotify("session launch"))
                    return;

                _starter = starter;
                SanitizeEquipmentRostersSafe("session launch");
                if (_menuRegistrationComplete)
                    return;

                AddMenuSafe();
                _menuRegistrationComplete = true;
                //DumpNordTroopIdsToFile();
                //DumpVlandianPirateIdsToFile();
            }
            catch (Exception e)
            {
                DisableMod("OnSessionLaunched failed.", e);
            }
        }

        private void OnPlayerSiegeStartedSafe()
        {
            try
            {
                if (_disabled) return;
                SetContextPause("Player entered siege flow.");
                NotifyContextPaused("player siege started");
            }
            catch
            {
                // never crash in safety callback
            }
        }

        private void OnMapEventStartedSafe(MapEvent mapEvent, PartyBase attackerParty, PartyBase defenderParty)
        {
            try
            {
                if (_disabled || mapEvent == null)
                    return;

                if (mapEvent.IsPlayerMapEvent)
                {
                    SetContextPause("Player map event started (" + mapEvent.EventType + ").");
                    NotifyContextPaused("map event started");
                }
            }
            catch (Exception e)
            {
                DisableMod("OnMapEventStarted safety callback failed.", e);
            }
        }

        private void OnMapEventEndedSafe(MapEvent mapEvent)
        {
            try
            {
                if (_disabled) return;
                TryResumeFromContextPause("map event ended");
            }
            catch (Exception e)
            {
                DisableMod("OnMapEventEnded safety callback failed.", e);
            }
        }

        private void OnDailyTickSafe()
        {
            try
            {
                if (_disabled) return;
                SanitizeEquipmentRostersSafe("daily tick");
            }
            catch (Exception e)
            {
                DisableMod("OnDailyTick safety callback failed.", e);
            }
        }

        private void SanitizeEquipmentRostersSafe(string context)
        {
            try
            {
                if (EquipmentRosterEquipmentsField == null)
                    return;

                if (Campaign.Current == null)
                    return;

                MBReadOnlyList<MBEquipmentRoster> rosters = TaleWorlds.CampaignSystem.Extensions.MBEquipmentRosterExtensions.All;
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
                    string message =
                        "Equipment roster cleanup (" + (context ?? "unknown") + "): replaced " +
                        replacedCount + " null entries across " + rosterCount + " rosters.";
                    AppendSafetyLog(message);
                    SafeLog("[BuyTroops] " + message);
                }
            }
            catch (Exception e)
            {
                DisableMod("Equipment roster cleanup failed.", e);
            }
        }

        private Equipment BuildEquipmentFallbackSafe(MBEquipmentRoster roster, IList equipments)
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

        private void OnSiegeEngineDestroyedSafe(MobileParty besiegerParty, Settlement settlement, BattleSideEnum side, SiegeEngineType siegeEngineType)
        {
            try
            {
                string settlementId = settlement != null ? settlement.StringId : "(null)";
                string siegeEngine = siegeEngineType != null ? siegeEngineType.ToString() : "(null)";
                AppendSafetyLog("OnSiegeEngineDestroyed: settlement=" + settlementId + ", side=" + side + ", engine=" + siegeEngine);

                if (!_disabled)
                {
                    SetContextPause("Siege engine destruction detected.");
                    NotifyContextPaused("siege engine destroyed");
                }
            }
            catch (Exception e)
            {
                DisableMod("OnSiegeEngineDestroyed safety callback failed.", e);
            }
        }

        private void OnMenuOpened(MenuCallbackArgs args)
        {
            try
            {
                if (_disabled)
                {
                    NotifyDisabled("menu open");
                    return;
                }

                if (IsSiegeContextUnsafe(args))
                {
                    SetContextPause("Unsafe combat/siege state detected while menu opened.");
                    NotifyContextPaused("menu open");
                    return;
                }

                TryResumeFromContextPause("menu open");

                string menuId = GetMenuIdSafe(args) ?? "(unknown)";

                if (EnableMenuIdDebug)
                    SafeLog("[BuyTroops] MenuId=" + menuId);
            }
            catch (Exception e)
            {
                DisableMod("OnMenuOpened failed.", e);
            }
        }

        private void DumpVlandianPirateIdsToFile()
        {
            try
            {
                LogToFile("===== VLANDIAN PIRATE DUMP START =====");

                foreach (CharacterObject c in CharacterObject.All)
                {
                    if (c == null || string.IsNullOrEmpty(c.StringId) || c.Culture == null)
                        continue;

                    if (!string.Equals(c.Culture.StringId, "vlandia", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string id = c.StringId.ToLowerInvariant();
                    if (id.Contains("sea") || id.Contains("pirate") || id.Contains("corsair") || id.Contains("naut") || id.Contains("sail") || id.Contains("marine"))
                    {
                        LogToFile(c.StringId);
                    }
                }

                LogToFile("===== VLANDIAN PIRATE DUMP END =====");
            }
            catch (Exception e)
            {
                LogToFile("ERROR dumping vlandian pirate troops: " + e.Message);
            }
        }

        private object GetPropertyValueSafe(object obj, string propertyName)
        {
            if (obj == null || string.IsNullOrEmpty(propertyName)) return null;

            try
            {
                Type t = obj.GetType();
                PropertyInfo pi = t.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null) return null;
                return pi.GetValue(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private string GetStringPropertySafe(object obj, params string[] propertyNames)
        {
            if (obj == null || propertyNames == null || propertyNames.Length == 0) return null;

            try
            {
                Type t = obj.GetType();
                foreach (string name in propertyNames)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    PropertyInfo pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (pi == null) continue;
                    object val = pi.GetValue(obj, null);
                    string s = val as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }


        private void AddMenuSafe()
        {
            try
            {
                if (IsDisabledAndNotify("add menus"))
                    return;

                if (_starter == null)
                {
                    DisableMod("Menu registration failed: campaign starter was null.");
                    return;
                }

                _starter.AddGameMenuOption(
                    "town",
                    "buy_troops_open",
                    "Hire Retinue",
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            if (ShouldBlockMenuAction(args, "town hire retinue condition"))
                                return false;

                            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                            args.IsEnabled = true;
                            return true;
                        }
                        catch (Exception e)
                        {
                            DisableMod("Town hire retinue condition crashed.", e);
                            return false;
                        }
                    },
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            if (ShouldBlockMenuAction(args, "town hire retinue consequence"))
                                return;

                            GameMenu.SwitchToMenu(ModMenuName);
                        }
                        catch (Exception e)
                        {
                            DisableMod("Town hire retinue consequence crashed.", e);
                        }
                    },
                    false,
                    6
                );

                if (IsWarSailsLoadedSafe())
                {
                    _starter.AddGameMenuOption(
                        "port_menu",
                        "buy_troops_pirate_port",
                        "Hire Pirate Crew (16 : 3k)",
                        delegate (MenuCallbackArgs args)
                        {
                            try
                            {
                                if (ShouldBlockMenuAction(args, "port pirate condition"))
                                    return false;

                                args.optionLeaveType = GameMenuOption.LeaveType.ForceToGiveTroops;
                                return true;
                            }
                            catch (Exception e)
                            {
                                DisableMod("Port pirate condition crashed.", e);
                                return false;
                            }
                        },
                        delegate (MenuCallbackArgs args)
                        {
                            try
                            {
                                if (ShouldBlockMenuAction(args, "port pirate consequence"))
                                    return;

                                PurchaseRetinue("pirate", 3000);
                                GameMenu.SwitchToMenu("port_menu");
                            }
                            catch (Exception e)
                            {
                                DisableMod("Port pirate consequence crashed.", e);
                            }
                        },
                        false,
                        5
                    );
                }
                else
                {
                    AppendSafetyLog("WarSails module not detected. Skipping port menu option registration.");
                }

                _starter.AddGameMenu(ModMenuName, "There is a selection of retinues willing to join your party.",
                    delegate (MenuCallbackArgs args) { });

                AddMenuOptionSafe("Basic Retinue (50 : 10k)", "basic", 10000);
                AddMenuOptionSafe("Elite Cohort (80 : 30k)", "elite", 30000);
                AddMenuOptionSafe("Bandit Army (30 : 3k)", "bandit", 3000);
                AddMenuOptionSafe("Savage (1 : 500gp)", "fian", 500);

                _starter.AddGameMenuOption(
                    "castle",
                    "buy_troops_sword_sisters",
                    "Hire Sword Sisters (50 : 4k)",
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            if (ShouldBlockMenuAction(args, "castle sword sisters condition"))
                                return false;

                            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                            args.IsEnabled = true;
                            return true;
                        }
                        catch (Exception e)
                        {
                            DisableMod("Castle sword sisters condition crashed.", e);
                            return false;
                        }
                    },
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            if (ShouldBlockMenuAction(args, "castle sword sisters consequence"))
                                return;

                            PurchaseRetinue("sisters", 4000);
                            GameMenu.SwitchToMenu("castle");
                        }
                        catch (Exception e)
                        {
                            DisableMod("Castle sword sisters consequence crashed.", e);
                        }
                    },
                    false,
                    6
                );

                _starter.AddGameMenuOption(
                    ModMenuName,
                    "buy_troops_leave",
                    "Leave",
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    },
                    delegate (MenuCallbackArgs args)
                    {
                        try { GameMenu.SwitchToMenu("town"); }
                        catch { }
                    },
                    false,
                    4,
                    false
                );
            }
            catch (Exception e)
            {
                DisableMod("AddMenuSafe failed while registering menu options.", e);
            }
        }

        private void AddMenuOptionSafe(string title, string type, int cost)
        {
            try
            {
                if (IsDisabledAndNotify("add menu option " + type))
                    return;

                if (_starter == null)
                {
                    DisableMod("AddMenuOptionSafe failed for '" + (type ?? "(null)") + "': starter was null.");
                    return;
                }

                _starter.AddGameMenuOption(
                    ModMenuName,
                    "buy_troops_" + type,
                    title,
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            if (ShouldBlockMenuAction(args, "retinue option condition " + type))
                                return false;

                            string cultureKey = GetCultureKeySafe();
                            args.MenuTitle = new TextObject("Recruit " + cultureKey + " " + title);
                            args.optionLeaveType = GameMenuOption.LeaveType.ForceToGiveTroops;
                            return true;
                        }
                        catch (Exception e)
                        {
                            DisableMod("Retinue option condition crashed for '" + (type ?? "(null)") + "'.", e);
                            return false;
                        }
                    },
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            if (ShouldBlockMenuAction(args, "retinue option consequence " + type))
                                return;

                            PurchaseRetinue(type, cost);
                            GameMenu.SwitchToMenu("town");
                        }
                        catch (Exception e)
                        {
                            DisableMod("Retinue option consequence crashed for '" + (type ?? "(null)") + "'.", e);
                        }
                    },
                    false,
                    0,
                    false
                );
            }
            catch (Exception e)
            {
                DisableMod("AddMenuOptionSafe failed for '" + (type ?? "(null)") + "'.", e);
            }
        }

        private void AddMenuOptionConditionalSafe(string title, string type, int cost, Func<bool> isEnabled)
        {
            try
            {
                if (IsDisabledAndNotify("add conditional menu option " + type))
                    return;

                if (_starter == null)
                {
                    DisableMod("AddMenuOptionConditionalSafe failed for '" + (type ?? "(null)") + "': starter was null.");
                    return;
                }

                _starter.AddGameMenuOption(
                    ModMenuName,
                    "buy_troops_" + type,
                    title,
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            if (ShouldBlockMenuAction(args, "conditional option condition " + type))
                                return false;

                            if (isEnabled != null && !isEnabled())
                                return false;

                            string cultureKey = GetCultureKeySafe();
                            args.MenuTitle = new TextObject("Recruit " + cultureKey + " " + title);
                            args.optionLeaveType = GameMenuOption.LeaveType.ForceToGiveTroops;
                            return true;
                        }
                        catch (Exception e)
                        {
                            DisableMod("Conditional option condition crashed for '" + (type ?? "(null)") + "'.", e);
                            return false;
                        }
                    },
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            if (ShouldBlockMenuAction(args, "conditional option consequence " + type))
                                return;

                            PurchaseRetinue(type, cost);
                            GameMenu.SwitchToMenu("town");
                        }
                        catch (Exception e)
                        {
                            DisableMod("Conditional option consequence crashed for '" + (type ?? "(null)") + "'.", e);
                        }
                    },
                    false,
                    0,
                    false
                );
            }
            catch (Exception e)
            {
                DisableMod("AddMenuOptionConditionalSafe failed for '" + (type ?? "(null)") + "'.", e);
            }
        }
    }
}
