using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
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
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            try
            {
                Campaign campaign = game.GameType as Campaign;
                if (campaign == null) return;

                CampaignGameStarter starter = gameStarterObject as CampaignGameStarter;
                if (starter == null) return;

                _buyMenuBehavior = new AddBuyMenu();
                starter.AddBehavior(_buyMenuBehavior);
            }
            catch
            {
                // never crash on load
            }
        }
    }

    public class AddBuyMenu : CampaignBehaviorBase
    {
        private const string ModMenuName = "elite_retinue_mod";
        private const string DefaultFactionKey = "Empire";
        private const string WarSailsModuleId = "WarSails";
        private const bool EnableMenuIdDebug = false;

        private CampaignGameStarter _starter;

        /* ===================== EVENTS ===================== */

        public override void RegisterEvents()
        {
            try
            {
                CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(OnSessionLaunched));
                CampaignEvents.GameMenuOpened.AddNonSerializedListener(this, new Action<MenuCallbackArgs>(OnMenuOpened));
            }
            catch
            {
                // swallow
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
            catch
            {
                // swallow
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
                        TryAddTroop("sturgian_line_breaker", 1);
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
            catch
            {
                // swallow
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
            catch
            {
                // swallow
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
                _starter = starter;
                AddMenuSafe();
                //DumpNordTroopIdsToFile();
                //DumpVlandianPirateIdsToFile();
            }
            catch
            {
                // swallow
            }
        }

        private void OnMenuOpened(MenuCallbackArgs args)
        {
            try
            {
                string menuId = "(unknown)";

                object ctx = GetPropertyValueSafe(args, "MenuContext");
                object gm = GetPropertyValueSafe(ctx, "GameMenu");

                string gmId = GetStringPropertySafe(gm, "StringId", "Id", "MenuId", "MenuStringId");
                if (!string.IsNullOrEmpty(gmId))
                    menuId = gmId;
                else
                {
                    string ctxId = GetStringPropertySafe(ctx, "StringId", "Id", "MenuId", "MenuStringId");
                    if (!string.IsNullOrEmpty(ctxId))
                        menuId = ctxId;
                    else
                    {
                        string argId = GetStringPropertySafe(args, "MenuId", "MenuStringId", "StringId", "Id");
                        if (!string.IsNullOrEmpty(argId))
                            menuId = argId;
                    }
                }

                if (EnableMenuIdDebug)
                    SafeLog("[BuyTroops] MenuId=" + menuId);
            }
            catch
            {
                // swallow
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
                if (_starter == null) return;

                _starter.AddGameMenuOption(
                    "town",
                    "buy_troops_open",
                    "Hire Retinue",
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            args.optionLeaveType = GameMenuOption.LeaveType.DefendAction;
                            args.IsEnabled = true;
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    },
                    delegate (MenuCallbackArgs args)
                    {
                        try { GameMenu.SwitchToMenu(ModMenuName); }
                        catch { }
                    },
                    false,
                    6
                );

                _starter.AddGameMenuOption(
                    "port_menu",
                    "buy_troops_pirate_port",
                    "Hire Pirate Crew (16 : 3k)",
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            args.optionLeaveType = GameMenuOption.LeaveType.ForceToGiveTroops;
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    },
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            PurchaseRetinue("pirate", 3000);
                            GameMenu.SwitchToMenu("port_menu");
                        }
                        catch
                        {
                            // swallow
                        }
                    },
                    false,
                    5
                );

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
                            args.optionLeaveType = GameMenuOption.LeaveType.DefendAction;
                            args.IsEnabled = true;
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    },
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            PurchaseRetinue("sisters", 4000);
                            GameMenu.SwitchToMenu("castle");
                        }
                        catch
                        {
                            // swallow
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
            catch
            {
                // swallow
            }
        }

        private void AddMenuOptionSafe(string title, string type, int cost)
        {
            try
            {
                if (_starter == null) return;

                _starter.AddGameMenuOption(
                    ModMenuName,
                    "buy_troops_" + type,
                    title,
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            string cultureKey = GetCultureKeySafe();
                            args.MenuTitle = new TextObject("Recruit " + cultureKey + " " + title);
                            args.optionLeaveType = GameMenuOption.LeaveType.ForceToGiveTroops;
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    },
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            PurchaseRetinue(type, cost);
                            GameMenu.SwitchToMenu("town");
                        }
                        catch
                        {
                            // swallow
                        }
                    },
                    false,
                    0,
                    false
                );
            }
            catch
            {
                // swallow
            }
        }

        private void AddMenuOptionConditionalSafe(string title, string type, int cost, Func<bool> isEnabled)
        {
            try
            {
                if (_starter == null) return;

                _starter.AddGameMenuOption(
                    ModMenuName,
                    "buy_troops_" + type,
                    title,
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            if (isEnabled != null && !isEnabled())
                                return false;

                            string cultureKey = GetCultureKeySafe();
                            args.MenuTitle = new TextObject("Recruit " + cultureKey + " " + title);
                            args.optionLeaveType = GameMenuOption.LeaveType.ForceToGiveTroops;
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    },
                    delegate (MenuCallbackArgs args)
                    {
                        try
                        {
                            PurchaseRetinue(type, cost);
                            GameMenu.SwitchToMenu("town");
                        }
                        catch
                        {
                            // swallow
                        }
                    },
                    false,
                    0,
                    false
                );
            }
            catch
            {
                // swallow
            }
        }
    }
}
