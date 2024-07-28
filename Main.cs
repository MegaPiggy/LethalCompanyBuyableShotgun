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
using LethalLevelLoader;

namespace BuyableShotgun
{
    [BepInDependency("evaisa.lethallib", "0.13.2")]
    [BepInPlugin(modGUID, modName, modVersion)]
    public class BuyableShotgun : BaseUnityPlugin
    {
        private const string modGUID = "MegaPiggy.BuyableShotgun";
        private const string modName = "Buyable Shotgun";
        private const string modVersion = "1.2.1";

        private readonly Harmony harmony = new Harmony(modGUID);

        private static BuyableShotgun Instance;

        private static ManualLogSource LoggerInstance => Instance.Logger;

        public static ExtendedItem duplicateShotgunExtendedItem { get; private set; }

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
            LethalLevelLoader.Plugin.onBeforeSetup += RegisterDuplicateShotgun;
            Logger.LogInfo($"Plugin {modName} is loaded with version {modVersion}!");
        }

        private void RegisterDuplicateShotgun()
        {
            Item vanillaShotgunItem = OriginalContent.StartOfRound.allItemsList.itemsList.FirstOrDefault(item => item.name.Equals("Shotgun"));
            Item duplicateShotgunItem = Instantiate(vanillaShotgunItem);

            duplicateShotgunItem.isScrap = false;

            ExtendedMod newExtendedMod = ExtendedMod.Create("Buyable Shotgun", "MegaPiggy");
            duplicateShotgunExtendedItem = ExtendedItem.Create(duplicateShotgunItem, newExtendedMod, ContentType.Custom);
            duplicateShotgunExtendedItem.IsBuyableItem = true;
            duplicateShotgunExtendedItem.CreditsWorth = ShotgunPrice;
            duplicateShotgunExtendedItem.OverrideInfoNodeDescription = "Nutcracker's Shotgun. Can hold 2 shells. Recommended to keep safety on while not using or it might shoot randomly.";

            PatchedContent.RegisterExtendedMod(newExtendedMod);
        }

        private static void UpdateShopItemPrice()
        {
            duplicateShotgunExtendedItem.CreditsWorth = ShotgunPrice;
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
            [HarmonyPatch(typeof(GameNetworkManager), "StartDisconnect")]
            public static void ServerDisconnect()
            {
                LoggerInstance.LogInfo("Server disconnect");
                ShotgunPriceRemote = -1;
            }
        }
    }
}