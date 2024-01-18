using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LethalLib.Modules;
using UnityEngine.SceneManagement;
using BepInEx.Configuration;
using Dissonance;

namespace BuyableShotgun
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class BuyableShotgun : BaseUnityPlugin
    {
        private const string modGUID = "MegaPiggy.BuyableShotgun";
        private const string modName = "BuyableShotgun";
        private const string modVersion = "1.0.1";

        private readonly Harmony harmony = new Harmony(modGUID);

        private static BuyableShotgun Instance;

        private static ManualLogSource LoggerInstance => Instance.Logger;

        public List<Item> AllItems => Resources.FindObjectsOfTypeAll<Item>().Concat(UnityEngine.Object.FindObjectsByType<Item>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)).ToList();
        public Item Shotgun => AllItems.FirstOrDefault(item => item.name == "Shotgun");
        public Item ShotgunClone { get; private set; }


        private ConfigEntry<int> ShotgunPriceConfig;
        public int ShotgunPrice => ShotgunPriceConfig.Value;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            harmony.PatchAll();
            ShotgunPriceConfig = Config.Bind("Prices", "ShotgunPrice", 700, "Credits needed to buy shotgun");
            SceneManager.sceneLoaded += OnSceneLoaded;
            Logger.LogInfo($"Plugin {modName} is loaded with version {modVersion}!");
        }

        private Item MakeNonScrap(Item original, int price)
        {
            Item clone = Object.Instantiate<Item>(original);
            clone.name = "Buyable" + original.name;
            clone.isScrap = false;
            clone.creditsWorth = price;
            return clone;
        }

        private TerminalNode CreateInfoNode(string name, string description)
        {
            TerminalNode node = ScriptableObject.CreateInstance<TerminalNode>();
            node.clearPreviousText = true;
            node.name = name + "InfoNode";
            node.displayText = description + "\n\n";
            return node;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Shotgun == null) return;
            if (ShotgunClone == null) ShotgunClone = MakeNonScrap(Shotgun, ShotgunPrice);
            Items.RegisterShopItem(ShotgunClone, price: ShotgunPrice, itemInfo: CreateInfoNode("Shotgun", "Nutcracker's shotgun. Can hold 2 shells. Recommended to keep safety on while not using or it might shoot randomly."));
            LoggerInstance.LogInfo($"Shotgun added to Shop for {ShotgunPrice} credits");
        }
    }
}