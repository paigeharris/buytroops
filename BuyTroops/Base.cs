using System;
using System.Collections.Generic;
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

        private CampaignGameStarter _starter;

        /* ===================== EVENTS ===================== */

        public override void RegisterEvents()
        {
            try
            {
                CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(OnSessionLaunched));
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
            }
            catch
            {
                // swallow
            }
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

                _starter.AddGameMenu(ModMenuName, "There is a selection of retinues willing to join your party.",
                    delegate (MenuCallbackArgs args) { });

                AddMenuOptionSafe("Basic Retinue (50 : 10k)", "basic", 10000);
                AddMenuOptionSafe("Elite Cohort (80 : 50k)", "elite", 50000);
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
    }
}
