using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine.Screens;
using TaleWorlds.Library;
using TaleWorlds.InputSystem;


namespace BuyTroops
{


    public class Base : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
           // Module.CurrentModule.AddInitialStateOption(new InitialStateOption("Message",new TextObject("Buy Troops", null),
            //    9990,
              //  () => {
                //    InformationManager.DisplayMessage(new InformationMessage("Oh we buying you some troops"));

                //},
               // false));

           
        }

        private AddBuyMenu _buyMenuBehavior;

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {

            Campaign campaign = game.GameType as Campaign;
            if (campaign == null) return;


            CampaignGameStarter gameInitializer = (CampaignGameStarter)gameStarterObject;
            AddBehaviours(gameInitializer);
        }

        private void AddBehaviours(CampaignGameStarter gameInitializer)
        {
            _buyMenuBehavior = new AddBuyMenu();

            gameInitializer.AddBehavior(_buyMenuBehavior);

        }

    }


    public class AddBuyMenu : CampaignBehaviorBase {
    
        string modMenuName = "elite_retinue_mod";
        CampaignGameStarter _obj = null;


        public void modLog(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage(message));
        }


        public void AddTroopsById(string id, int amount)
        {
            amount = Convert.ToBoolean(amount) ? amount : 1;
            MobileParty.MainParty.AddElementToMemberRoster(CharacterObject.Find(id), amount, false);

        }

        public void AddEliteRetinueOld()
        {
            
            AddTroopsById("imperial_elite_cataphract", 10);
            AddTroopsById("bucellarii", 15);
            AddTroopsById("imperial_legionary", 15);
            AddTroopsById("legion_of_the_betrayed_tier_3", 10);
            AddTroopsById("battanian_fian_champion", 3);
            AddTroopsById("wolfskins_tier_3", 2);
            AddTroopsById("imperial_sergeant_crossbowman", 15);
            AddTroopsById("imperial_veteran_archer", 10);
        }

        public void AddBasicRetinueOld()
        {
            AddTroopsById("bucellarii", 5);
            AddTroopsById("imperial_equite", 10);
            AddTroopsById("legion_of_the_betrayed_tier_3", 5);
            AddTroopsById("imperial_crossbowman", 10);
            AddTroopsById("imperial_trained_archer", 10);
            AddTroopsById("imperial_trained_infantryman", 10);
        }

        public void AddBanditRetinueOld()
        {
            AddTroopsById("steppe_bandits_chief", 5);
            AddTroopsById("mountain_bandits_raider", 5);
            AddTroopsById("forest_bandits_raider", 5);
            AddTroopsById("sea_raiders_raider", 5);
            AddTroopsById("desert_bandits_raider", 5);
            AddTroopsById("forest_bandits_bandit", 5);
        }

        public void AddEliteRetinue(FactionTroops troops)
        {
            AddTroopsById(troops.horse_archers, 15);
            AddTroopsById(troops.cavalry, 10);
            AddTroopsById(troops.archers, 20);
            AddTroopsById(troops.infantry, 15);
            AddTroopsById(troops.wildcard1, 10);
            AddTroopsById(troops.wildcard2, 5);
            AddTroopsById(troops.wildcard3, 5);
        }

        public void AddBasicRetinue(FactionTroops troops)
        {
            AddTroopsById(troops.horse_archers, 10);
            AddTroopsById(troops.cavalry, 10);
            AddTroopsById(troops.archers, 15);
            AddTroopsById(troops.infantry, 10);
            AddTroopsById(troops.wildcard1, 5);
        }

        public void AddBanditRetinue(FactionTroops troops)
        {
            AddTroopsById(troops.horse_archers, 5);
            AddTroopsById(troops.cavalry, 5);
            AddTroopsById(troops.archers, 10);
            AddTroopsById(troops.infantry, 5);
            AddTroopsById(troops.wildcard1, 5);
        }

        public class FactionTroops
        {
            public string infantry, archers, cavalry, horse_archers, wildcard1, wildcard2, wildcard3;

            public FactionTroops(string _infantry, string _archers, string _cavalry, string _horse_archers, string _wildcard1, string _wildcard2, string _wildcard3)
            {
                infantry = _infantry;
                archers = _archers;
                cavalry = _cavalry;
                horse_archers = _horse_archers;
                wildcard1 = _wildcard1;
                wildcard2 = _wildcard2;
                wildcard3 = _wildcard3;
            }


            public FactionTroops(string _infantry, string _archers, string _cavalry, string _horse_archers)
            {
                infantry = _infantry;
                archers = _archers;
                cavalry = _cavalry;
                horse_archers = _horse_archers;
                wildcard1 = _archers;
                wildcard2 = _horse_archers;
                wildcard3 = _infantry;
            }

            public FactionTroops(string _infantry, string _archers, string _cavalry, string _horse_archers, string _wildcard1)
            {
                infantry = _infantry;
                archers = _archers;
                cavalry = _cavalry;
                horse_archers = _horse_archers;
                wildcard1 = _wildcard1;
                wildcard2 = _wildcard1;
                wildcard3 = _wildcard1;
            }

            public FactionTroops(string _infantry, string _archers, string _cavalry, string _horse_archers, string _wildcard1, string _wildcard2)
            {
                infantry = _infantry;
                archers = _archers;
                cavalry = _cavalry;
                horse_archers = _horse_archers;
                wildcard1 = _wildcard1;
                wildcard2 = _wildcard2;
                wildcard3 = _wildcard2;
            }
        }


        public static Dictionary<string, FactionTroops> factions = new Dictionary<string, FactionTroops>()
        {
            { "Empire", new FactionTroops("imperial_legionary", "imperial_palatine_guard", "imperial_elite_cataphract", "bucellarii", "legion_of_the_betrayed_tier_3", "imperial_sergeant_crossbowman","imperial_elite_menavliaton") },
            { "Sturgia",  new FactionTroops("sturgian_shock_troop", "sturgian_veteran_bowman", "druzhinnik_champion", "sturgian_horse_raider", "skolderbrotva_tier_3", "sturgian_ulfhednar") },
            { "Battania",  new FactionTroops("battanian_veteran_falxman", "battanian_fian_champion", "battanian_horseman", "battanian_mounted_skirmisher", "battanian_oathsworn", "battanian_fian_champion") },
            { "Vlandia", new FactionTroops("vlandian_sergeant", "vlandian_sharpshooter", "vlandian_banner_knight", "vlandian_vanguard", "vlandian_voulgier", "vlandian_pikeman") },
            { "Aserai",  new FactionTroops("aserai_veteran_infantry", "aserai_master_archer", "aserai_vanguard_faris", "aserai_mameluke_heavy_cavalry", "ghilman_tier_3", "mamluke_palace_guard",  "beni_zilal_tier_3") },
            { "Khuzait",  new FactionTroops("khuzait_darkhan", "khuzait_marksman", "khuzait_heavy_lancer", "khuzait_khans_guard", "karakhuzaits_tier_3","khuzait_marksman") },
            { "Bandits",  new FactionTroops("sea_raiders_raider", "forest_bandits_raider", "desert_bandits_raider", "steppe_bandits_chief", "mountain_bandits_raider") },
        };


        private string GetCulture()
        {
            return Settlement.CurrentSettlement.Culture.ToString();
        }


        private void AddRetinue(string type)
        {
            //
            FactionTroops troops = factions[GetCulture()];

            if (type == "elite")
            {
                AddEliteRetinue(troops);
            } else if (type == "basic" )
            {
                AddBasicRetinueOld();
            } else if (type == "bandit")
            {
                AddBanditRetinue(factions["Bandits"]);
            }
            else if (type == "fian")
            {
                AddTroopsById("battanian_fian_champion", 1);
            }
            else
            {
                AddTroopsById("vlandian_banner_knight", 1);
            }

        }

        private void PurchaseRetinue(string type, int cost)
        {


            if (Hero.MainHero.Gold >= cost)
            {
                modLog("Recruting " + type + " retinue for " + cost + " denars..");
                Hero.MainHero.ChangeHeroGold(-cost);
                AddRetinue(type);
            }
            else
            {
                modLog("Not enough denars. " + cost + " required to recruit " + type + " retinue..");
            }
        }


  
        private void AddModMenu()
        {
            _obj.AddGameMenuOption("town", "town_enter_entr_option", "Hire Elite Retinue",
          (MenuCallbackArgs args) =>
          {
              args.optionLeaveType = GameMenuOption.LeaveType.DefendAction;
              //args.IsEnabled = Hero.MainHero.Gold >= 3000;
              args.IsEnabled = true;
              return true;
          },
         (MenuCallbackArgs args) => {
             GameMenu.SwitchToMenu(modMenuName);
         }, false, 6);

            _obj.AddGameMenu(modMenuName, "There is a selection of retinues willing to join your party.",
                (MenuCallbackArgs args) =>
                {
                });
        }

        private void AddModMenuOption(string title, string type, int cost)
        {

            _obj.AddGameMenuOption(modMenuName, "town_enter_entr_option", title,
             (MenuCallbackArgs args) =>
             {
                 string menuTitle = "Recruit " + GetCulture()+ " " + title;

                 args.MenuTitle = new TextObject(menuTitle);

                 args.optionLeaveType = GameMenuOption.LeaveType.ForceToGiveTroops;
                 return (type != "basic" || GetCulture() == "Empire");
             },
             (MenuCallbackArgs args) =>
             {
                 PurchaseRetinue(type, cost);
                 GameMenu.SwitchToMenu("town");
             }, false, 0, false);
        }

        private void AddMenuOptions()
        {
            AddModMenuOption("Savage (1 : 500gp)", "fian", 500);
            AddModMenuOption("Bandit Army (30 : 3k)", "bandit", 3000);
            AddModMenuOption("Basic Retinue (50 : 10k)", "basic", 10000);
            AddModMenuOption("Elite Cohort (80 : 50k)", "elite", 50000);

            _obj.AddGameMenuOption(modMenuName, "town_enter_entr_option", "Leave",
            (MenuCallbackArgs args) =>
            {
                args.optionLeaveType = GameMenuOption.LeaveType.Leave;
                return true;
            },
            (MenuCallbackArgs args) =>
            {
                GameMenu.SwitchToMenu("town");
            }, false, 4, false);

        }


        private void OnSessionLaunched(CampaignGameStarter obj)
        {
            _obj = obj;

            AddModMenu();
            AddMenuOptions();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this,
                new Action<CampaignGameStarter>(this.OnSessionLaunched));
        }


        public override void SyncData(IDataStore dataStore)
        {
   
        }

    }
}
