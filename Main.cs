using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LethalLib.Modules;
using UnityEngine.SceneManagement;
using BepInEx.Configuration;
using Unity.Netcode;
using System.Reflection;
using System;
using NetworkPrefabs = LethalLib.Modules.NetworkPrefabs;
using Unity.Collections;
using GameNetcodeStuff;

namespace BuyableShotgun
{
    [BepInDependency("evaisa.lethallib", "0.13.2")]
    [BepInPlugin(modGUID, modName, modVersion)]
    public class BuyableShotgun : BaseUnityPlugin
    {
        private const string modGUID = "MegaPiggy.BuyableShotgun";
        private const string modName = "Buyable Shotgun";
        private const string modVersion = "1.3.0";

        private readonly Harmony harmony = new Harmony(modGUID);

        private static BuyableShotgun Instance;

        private static ManualLogSource LoggerInstance => Instance.Logger;

        public static StartOfRound StartOfRound => StartOfRound.Instance;
        public static List<Item> AllItems => StartOfRound.allItemsList.itemsList.ToList();
        public static Item Shotgun => AllItems.FirstOrDefault(item => item.name.Equals("Shotgun") && item.spawnPrefab != null);
        public static Item ShotgunClone { get; private set; }
        public static GameObject ShotgunObjectClone { get; private set; }

        private static ConfigEntry<int> ShotgunPriceConfig;
        public static int ShotgunPriceLocal => ShotgunPriceConfig.Value;
        internal static int ShotgunPriceRemote = -1;
        public static int ShotgunPrice => ShotgunPriceRemote > -1 ? ShotgunPriceRemote : ShotgunPriceLocal;
        private static bool IsHost => NetworkManager.Singleton.IsHost;
        private static ulong LocalClientId => NetworkManager.Singleton.LocalClientId;

        private void Awake()
        {
            if (Instance == null)
            {
                DontDestroyOnLoad(this);
                Instance = this;
            }
            harmony.PatchAll();
            ShotgunPriceConfig = Config.Bind("Prices", "ShotgunPrice", 700, "Credits needed to buy shotgun");
            Logger.LogInfo($"Plugin {modName} is loaded with version {modVersion}!");
        }

        public class ClonedItem : Item
        {
            public Item original;
        }

        private static ClonedItem CloneNonScrap(Item original, int price)
        {
            ClonedItem clone = ScriptableObject.CreateInstance<ClonedItem>();
            DontDestroyOnLoad(clone);
            clone.original = original;
            var prefab = NetworkPrefabs.CloneNetworkPrefab(original.spawnPrefab, "Buyable" + original.name);
            prefab.AddComponent<Unflagger>();
            DontDestroyOnLoad(prefab);
            CopyFields(original, clone);
            prefab.GetComponent<GrabbableObject>().itemProperties = clone;
            clone.spawnPrefab = prefab;
            clone.name = "Buyable" + original.name;
            clone.creditsWorth = price;
            clone.isScrap = false;
            return clone;
        }

        public static void CopyFields(Item source, Item destination)
        {
            FieldInfo[] fields = typeof(Item).GetFields();
            foreach (FieldInfo field in fields)
            {
                field.SetValue(destination, field.GetValue(source));
            }
        }

        private static Dictionary<string, TerminalNode> infoNodes = new Dictionary<string, TerminalNode>();

        private static TerminalNode CreateInfoNode(string name, string description)
        {
            if (infoNodes.ContainsKey(name)) return infoNodes[name];
            TerminalNode node = ScriptableObject.CreateInstance<TerminalNode>();
            DontDestroyOnLoad(node);
            node.clearPreviousText = true;
            node.name = name + "InfoNode";
            node.displayText = description + "\n\n";
            infoNodes.Add(name, node);
            return node;
        }

        private static void CloneShotgun()
        {
            if (StartOfRound == null) return;
            if (AllItems == null) return;
            if (Shotgun == null) return;
            if (ShotgunClone != null) return;
            ShotgunClone = CloneNonScrap(Shotgun, ShotgunPrice);
            AddToShop();
        }

        private static void AddToShop()
        {
            Items.RegisterShopItem(ShotgunClone, price: ShotgunPrice, itemInfo: CreateInfoNode("Shotgun", "Nutcracker's Shotgun. Can hold 2 shells. Recommended to keep safety on while not using or it might shoot randomly."));
            LoggerInstance.LogInfo($"Shotgun added to Shop for {ShotgunPrice} credits");
        }

        private static void UpdateShopItemPrice()
        {
            ShotgunClone.creditsWorth = ShotgunPrice;
            Items.UpdateShopItemPrice(ShotgunClone, price: ShotgunPrice);
            LoggerInstance.LogInfo($"Shotgun price updated to {ShotgunPrice} credits");
        }

        public static byte CurrentVersionByte = 1;

        public static void WriteData(FastBufferWriter writer)
        {
            writer.WriteByte(CurrentVersionByte);
            writer.WriteBytes(BitConverter.GetBytes(ShotgunPriceLocal));
        }

        public static void ReadData(FastBufferReader reader)
        {
            reader.ReadByte(out byte version);
            if (version == CurrentVersionByte)
            {
                var priceBytes = new byte[4];
                reader.ReadBytes(ref priceBytes, 4);
                ShotgunPriceRemote = BitConverter.ToInt32(priceBytes, 0);
                UpdateShopItemPrice();
                LoggerInstance.LogInfo("Host config set successfully");
                return;
            }
            throw new Exception("Invalid version byte");
        }

        public static void OnRequestSync(ulong clientID, FastBufferReader reader)
        {
            if (IsHost)
            {
                LoggerInstance.LogInfo("Sending config to client " + clientID.ToString());
                FastBufferWriter writer = new FastBufferWriter(5, Allocator.Temp, 5);
                try
                {
                    WriteData(writer);
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("BuyableShotgun_OnReceiveConfigSync", clientID, writer, NetworkDelivery.Reliable);
                }
                catch (Exception ex)
                {
                    LoggerInstance.LogError($"Failed to send config: {ex}");
                }
                finally
                {
                    writer.Dispose();
                }
            }
        }

        public static void OnReceiveSync(ulong clientID, FastBufferReader reader)
        {
            LoggerInstance.LogInfo("Received config from host");
            try
            {
                ReadData(reader);
            }
            catch (Exception ex)
            {
                LoggerInstance.LogError($"Failed to receive config: {ex}");
                ShotgunPriceRemote = -1;
            }
        }

        /// <summary>
        /// For what ever reason the hide flags were set to HideAndDontSave, which caused it to not save obviously.
        /// I'm not sure what sets and I don't want to bother finding out when a fix like this is so easy.
        /// </summary>
        internal class Unflagger : MonoBehaviour
        {
            public void Awake()
            {
                gameObject.hideFlags = HideFlags.None;
            }
        }

        [HarmonyPatch]
        internal static class Patches
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
            public static void ServerConnect()
            {
                if (IsHost)
                {
                    LoggerInstance.LogInfo("Started hosting, using local settings");
                    NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler("BuyableShotgun_OnRequestConfigSync", OnRequestSync);
                    UpdateShopItemPrice();
                }
                else
                {
                    LoggerInstance.LogInfo("Connected to server, requesting settings");
                    NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler("BuyableShotgun_OnReceiveConfigSync", OnReceiveSync);
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("BuyableShotgun_OnRequestConfigSync", 0, new FastBufferWriter(0, Allocator.Temp), NetworkDelivery.Reliable);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(StartOfRound), "Awake")]
            public static void Awake()
            {
                LoggerInstance.LogWarning("Start of round awake");
                CloneShotgun();
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(GameNetworkManager), "StartDisconnect")]
            public static void ServerDisconnect()
            {
                LoggerInstance.LogInfo("Server disconnect");
                ShotgunPriceRemote = -1;
            }
        }
    }
}