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
        private const string modVersion = "1.2.1";

        private readonly Harmony harmony = new Harmony(modGUID);

        private static BuyableShotgun Instance;

        private static ManualLogSource LoggerInstance => Instance.Logger;

        public static List<Item> AllItems => Resources.FindObjectsOfTypeAll<Item>().Concat(UnityEngine.Object.FindObjectsByType<Item>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)).ToList();
        public static Item Shotgun => AllItems.FirstOrDefault(item => item.name.Equals("Shotgun") && item.spawnPrefab != null); // also check for spawn prefab because some mods add an extra without one for whatever reason
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
            SceneManager.sceneLoaded += OnSceneLoaded;
            ShotgunClone = MakeNonScrap(ShotgunPrice);
            AddToShop();
            Logger.LogInfo($"Plugin {modName} is loaded with version {modVersion}!");
        }

        private static Item MakeNonScrap(int price)
        {
            Item nonScrap = ScriptableObject.CreateInstance<Item>();
            DontDestroyOnLoad(nonScrap);
            nonScrap.name = "Error";
            nonScrap.itemName = "Error";
            nonScrap.itemId = 6624;
            nonScrap.isScrap = false;
            nonScrap.creditsWorth = price;
            nonScrap.canBeGrabbedBeforeGameStart = true;
            nonScrap.automaticallySetUsingPower = false;
            nonScrap.batteryUsage = 300;
            nonScrap.canBeInspected = false;
            nonScrap.isDefensiveWeapon = true;
            nonScrap.saveItemVariable = true;
            nonScrap.syncGrabFunction = false;
            nonScrap.twoHandedAnimation = true;
            nonScrap.verticalOffset = 0.25f;
            var prefab = LethalLib.Modules.NetworkPrefabs.CreateNetworkPrefab("Cube");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(prefab.transform, false);
            cube.GetComponent<MeshRenderer>().sharedMaterial.shader = Shader.Find("HDRP/Lit");
            prefab.AddComponent<BoxCollider>().size = Vector3.one * 2;
            prefab.AddComponent<AudioSource>();
            var prop = prefab.AddComponent<PhysicsProp>();
            prop.itemProperties = nonScrap;
            prop.grabbable = true;
            nonScrap.spawnPrefab = prefab;
            prefab.tag = "PhysicsProp";
            prefab.layer = LayerMask.NameToLayer("Props");
            cube.layer = LayerMask.NameToLayer("Props");
            try
            {
                GameObject scanNode = GameObject.Instantiate<GameObject>(Items.scanNodePrefab, prefab.transform);
                scanNode.name = "ScanNode";
                scanNode.transform.localPosition = new Vector3(0f, 0f, 0f);
                scanNode.transform.localScale *= 2;
                ScanNodeProperties properties = scanNode.GetComponent<ScanNodeProperties>();
                properties.nodeType = 1;
                properties.headerText = "Error";
                properties.subText = $"A mod is incompatible with {modName}";
            }
            catch (Exception e)
            {
                LoggerInstance.LogError(e.ToString());
            }
            prefab.transform.localScale = Vector3.one / 2;
            return nonScrap;
        }

        private static GameObject CloneNonScrap(Item original, Item clone, int price)
        {
            GameObject.Destroy(clone.spawnPrefab);
            var prefab = NetworkPrefabs.CloneNetworkPrefab(original.spawnPrefab);
            prefab.AddComponent<Unflagger>();
            DontDestroyOnLoad(prefab);
            CopyFields(original, clone);
            prefab.GetComponent<GrabbableObject>().itemProperties = clone;
            clone.spawnPrefab = prefab;
            clone.name = "Buyable" + original.name;
            clone.creditsWorth = price;
            return prefab;
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

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LoggerInstance.LogInfo("Scene \"" + scene.name + "\" loaded with " + mode + " mode.");
            CloneShotgun();
        }

        private static void CloneShotgun()
        {
            if (Shotgun == null) return;
            if (ShotgunObjectClone != null) return;
            ShotgunObjectClone = CloneNonScrap(Shotgun, ShotgunClone, ShotgunPrice);
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
                }
                else
                {
                    LoggerInstance.LogInfo("Connected to server, requesting settings");
                    NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler("BuyableShotgun_OnReceiveConfigSync", OnReceiveSync);
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("BuyableShotgun_OnRequestConfigSync", 0, new FastBufferWriter(0, Allocator.Temp), NetworkDelivery.Reliable);
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(GameNetworkManager), "Start")]
            public static void Start()
            {
                LoggerInstance.LogWarning("Game network manager start");
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