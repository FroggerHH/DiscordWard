﻿using BepInEx;
using BepInEx.Configuration;
using DiscordWebhook;
using fastJSON;
using HarmonyLib;
using LocalizationManager;
using ServerSync;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DiscordWard;

[BepInPlugin(ModGUID, ModName, ModVersion)]
internal class Plugin : BaseUnityPlugin
{
    #region values

    internal const string ModName = "DiscordWard", ModVersion = "1.1.0", ModGUID = "com.Frogger." + ModName;
    internal static Harmony harmony = new(ModGUID);

    internal static PrivateArea current;
    internal static string lastPrivateName;
    internal static PrivateType lastPrivateType;
    internal static Plugin _self;
    internal bool canSendWebHook = true;
    internal bool inZone = false;

    #endregion

    #region ConfigSettings

    #region tools

    static string ConfigFileName = "com.Frogger.DiscordWard.cfg";
    DateTime LastConfigChange;

    public static readonly ConfigSync configSync = new(ModName)
        { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

    private static ConfigEntry<Toggle> serverConfigLocked = null!;

    public static ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
        bool synchronizedSetting = true)
    {
        ConfigEntry<T> configEntry = _self.Config.Bind(group, name, value, description);

        SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, string description,
        bool synchronizedSetting = true)
    {
        return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }

    void SetCfgValue<T>(Action<T> setter, ConfigEntry<T> config)
    {
        setter(config.Value);
        config.SettingChanged += (_, _) => setter(config.Value);
    }

    public enum Toggle
    {
        On = 1,
        Off = 0
    }

    #endregion

    #region configs

    static ConfigEntry<string> moderatorUrlConfig;
    static ConfigEntry<string> logrUrlConfig;
    static ConfigEntry<string> languageServerConfig;
    static ConfigEntry<Toggle> preventItemDropPickupConfig;
    static ConfigEntry<Toggle> preventPickablePickupConfig;
    static ConfigEntry<Toggle> preventCraftingConfig;
    static ConfigEntry<float> webHookTimerConfig;
    static ConfigEntry<float> logWebHookTimerConfig;
    static ConfigEntry<bool> sendZoneMessagesConfig;

    internal static ConfigEntry<bool> sendPickupMessagesConfig;
    internal static ConfigEntry<bool> sendBeehiveMessagesConfig;
    internal static ConfigEntry<bool> sendChairMessagesConfig;
    internal static ConfigEntry<bool> sendChestMessagesConfig;
    internal static ConfigEntry<bool> sendCraftMessagesConfig;
    internal static ConfigEntry<bool> sendDoorMessagesConfig;
    internal static ConfigEntry<bool> sendFireplaceMessagesConfig;
    internal static ConfigEntry<bool> sendItemStandMessagesConfig;
    internal static ConfigEntry<bool> sendMapTableMessagesConfig;
    internal static ConfigEntry<bool> sendPickableMessagesConfig;
    internal static ConfigEntry<bool> sendSignMessagesConfig;
    internal static ConfigEntry<bool> sendTeleportMessagesConfig;
    internal static ConfigEntry<bool> sendGuardInteractMessagesConfig;
    internal static ConfigEntry<bool> sendDamageMessagesConfig;
    internal static ConfigEntry<bool> sendDestroyMessagesConfig;

    #endregion

    #region config values

    internal static string moderatorUrl = "";

    public static string languageServer = "English";

    //public static Localization localization = new();
    internal static string logrUrl = "";
    internal static bool preventItemDropPickup = false;
    internal static bool preventPickablePickup = false;
    internal static bool preventCrafting = false;
    internal static float webHookTimer = 2.5f;
    internal static float logWebHookTimer = 2f;
    internal static bool sendZoneMessages = true;

    #endregion

    #endregion

    #region Config

    private void SetupWatcher()
    {
        FileSystemWatcher fileSystemWatcher = new(Paths.ConfigPath, ConfigFileName);
        fileSystemWatcher.Changed += ConfigChanged;
        fileSystemWatcher.IncludeSubdirectories = true;
        fileSystemWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        fileSystemWatcher.EnableRaisingEvents = true;
    }

    void ConfigChanged(object sender, FileSystemEventArgs e)
    {
        if ((DateTime.Now - LastConfigChange).TotalSeconds <= 5.0)
        {
            return;
        }

        LastConfigChange = DateTime.Now;
        try
        {
            Config.Reload();
        }
        catch
        {
            DebugError("Can't reload Config", true);
        }
    }

    private void UpdateConfiguration()
    {
        languageServer = languageServerConfig.Value;
        try
        {
            localization.SetupLanguage(languageServer);
        }
        catch (Exception e)
        {
            DebugError($"Cant find language {languageServer}", false);
            localization = english;
        }

        sendZoneMessages = sendZoneMessagesConfig.Value;
        if (ZNet.m_isServer)
        {
            moderatorUrl = moderatorUrlConfig.Value;
            moderatorUrl = moderatorUrl.Replace(" ", "");
            Discord.SendWebhook();
        }

        //logrUrl = logrUrlConfig.Value;
        //localization.SetupLanguage(languageServer);
        //preventItemDropPickup = preventItemDropPickupConfig.Value == Toggle.On;
        //preventPickablePickup = preventPickablePickupConfig.Value == Toggle.On;
        //preventCrafting = preventCraftingConfig.Value == Toggle.On;
        //webHookTimer = webHookTimerConfig.Value;
        //logWebHookTimer = logWebHookTimerConfig.Value;
        //if (logrUrl != string.Empty) logrUrl = logrUrl.Replace(" ", "");
        //InvokeRepeating("LogWebHookTimer", logWebHookTimer, logWebHookTimer);


        Debug("Configuration Received");
    }

    #endregion

    #region tools

    public static void Debug(string msg, bool localize = false)
    {
        if (Localization.instance != null && localize)
        {
            _self.Logger.LogInfo(Localization.instance.Localize(msg));
        }
        else
        {
            _self.Logger.LogInfo(msg);
        }
    }

    public static void DebugError(object msg, bool showWriteToDev)
    {
        if (showWriteToDev)
        {
            msg += "Write to the developer and moderator if this happens often.";
        }

        _self.Logger.LogError(msg);
    }

    public static void DebugWarning(string msg, bool showWriteToDev)
    {
        if (showWriteToDev)
        {
            msg += "Write to the developer and moderator if this happens often.";
        }

        _self.Logger.LogWarning(msg);
    }

    #endregion

    internal static Localization english = null!;
    internal static Localization localization = null!;

    private void Awake()
    {
        _self = this;
        Localizer.Load();

        JSON.Parameters = new JSONParameters
        {
            UseExtensions = false,
            SerializeNullValues = false,
            DateTimeMilliseconds = false,
            UseUTCDateTime = true,
            UseOptimizedDatasetSchema = true,
            UseValuesOfEnums = true
        };

        #region config

        Config.SaveOnConfigSet = false;

        configSync.AddLockingConfigEntry(config("Main", "Lock Configuration", Toggle.On,
            "If on, the configuration is locked and can be changed by server admins only."));

        moderatorUrlConfig = config("Urls", "moderatorUrl", "",
            new ConfigDescription("Url of the moderator's webhook", null,
                new ConfigurationManagerAttributes
                    { HideSettingName = true, HideDefaultButton = true, Browsable = false }), false);
        sendZoneMessagesConfig =
            config("Main", "Send Zone Messages", sendZoneMessages, "Will messages be sent from zones");
        languageServerConfig = config("Main", "server language", "English",
            "The language in which the moderator receives notifications.", true);
        // logrUrlConfig = config("Urls", "logrUrl", "",
        //     new ConfigDescription(
        //         "It differs in that all messages come here, both from the actions of players on foreign shores and in their own.",
        //         null, new ConfigurationManagerAttributes { HideSettingName = true, HideDefaultButton = true }),
        //     true);
        // preventItemDropPickupConfig = config("Main", "preventItemDropPickup", Toggle.Off,
        //     "If you use a mod that already prevents ItemDropPickup in guard, you can leave Off. WORKS ONLY WITH VANILA WARD",
        //     true);
        // preventPickablePickupConfig = config("Main", "preventPickablePickup", Toggle.Off,
        //     "If you use a mod that already prevents PickablePickup in guard, you can leave Off. WORKS ONLY WITH VANILA WARD",
        //     true);
        // preventCraftingConfig = config("Main", "preventCrafting", Toggle.Off,
        //     "If you use a mod that already prevents using Crafting in guard, you can leave Off. WORKS ONLY WITH VANILA WARD",
        //     true);
        // webHookTimerConfig = config("Main", "webHookTimer", 0.5f,
        //     "This is the minimum time interval between sending webhooks.",
        //   true);
        // logWebHookTimerConfig = config("Main", "Log webHookTimer", 2f,
        //     "This is the minimum time interval between sending log webhooks.", true);

        sendPickupMessagesConfig = config("Filter", "Send Pickup messages", true, "");
        sendBeehiveMessagesConfig = config("Filter", "Send Beehive messages", true, "");
        sendChairMessagesConfig = config("Filter", "Send Chair messages", true, "");
        sendChestMessagesConfig = config("Filter", "Send Chest messages", true, "");
        sendCraftMessagesConfig = config("Filter", "Send Craft messages", true, "");
        sendDoorMessagesConfig = config("Filter", "Send Door messages", true, "");
        sendFireplaceMessagesConfig = config("Filter", "Send Fireplace messages", true, "");
        sendItemStandMessagesConfig = config("Filter", "Send ItemStand messages", true, "");
        sendMapTableMessagesConfig = config("Filter", "Send MapTable messages", true, "");
        sendPickableMessagesConfig = config("Filter", "Send Pickable messages", true, "");
        sendSignMessagesConfig = config("Filter", "Send Sign messages", true, "");
        sendTeleportMessagesConfig = config("Filter", "Send Teleport messages", true, "");
        sendGuardInteractMessagesConfig = config("Filter", "Send GuardInteract messages", true, "");
        sendDamageMessagesConfig = config("Filter", "Send Damage messages", true, "");
        sendDestroyMessagesConfig = config("Filter", "Send Destroy messages", true, "");

        Config.SaveOnConfigSet = true;

        #endregion

        english = new Localization();
        english.SetupLanguage("English");
        localization = english;


        SetupWatcher();
        Config.SettingChanged += (_, _) => { UpdateConfiguration(); };
        Config.ConfigReloaded += (_, _) => { UpdateConfiguration(); };

        Config.Save();

        InvokeRepeating(nameof(UdateCurrentPrivateZone), 3, 3);
        InvokeRepeating(nameof(EnterLeftGuard), 3, 3);

        harmony.PatchAll(typeof(AutoPickupPatch));
        harmony.PatchAll(typeof(BeehivePatch));
        harmony.PatchAll(typeof(ChairPatch));
        harmony.PatchAll(typeof(ContainerPatch));
        harmony.PatchAll(typeof(CraftingStationPatch));
        harmony.PatchAll(typeof(DoorPatch));
        harmony.PatchAll(typeof(FireplacePatch));
        harmony.PatchAll(typeof(ItemDropPatch));
        harmony.PatchAll(typeof(ItemStandPatch));
        harmony.PatchAll(typeof(MapTablePatch));
        harmony.PatchAll(typeof(PickableObjectPatch));
        harmony.PatchAll(typeof(SignPatch));
        harmony.PatchAll(typeof(TeleportPatch));
        harmony.PatchAll(typeof(WardPatch));
        harmony.PatchAll(typeof(WearNTearPatch));
        harmony.PatchAll(typeof(ZNetSceneStartPatch));
    }

    private void UdateCurrentPrivateZone()
    {
        bool isFocused = Application.isFocused;

        if (Player.m_localPlayer && isFocused)
        {
            current = null;
            float oldDistance = 9999;
            foreach (PrivateArea area in PrivateArea.m_allAreas)
            {
                if (area)
                {
                    float dist = Vector3.Distance(Player.m_localPlayer.transform.position,
                        area.transform.position);
                    if (dist < oldDistance && dist <= 40)
                    {
                        current = area;
                        oldDistance = dist;
                    }
                }
            }
        }
    }

    private void EnterLeftGuard()
    {
        bool isFocused = Application.isFocused;
        if (!isFocused || !canSendWebHook || !Player.m_localPlayer || string.IsNullOrEmpty(moderatorUrl) ||
            string.IsNullOrWhiteSpace(moderatorUrl)) return;
        //if (!Helper.GetCurrentAreaOwnerName(out string creatorName)) return;
        var playerName = Helper.GetPlayerName();
        DiscordWebhookData data = new("", $"{playerName} ");

        bool flag = Helper.CheckAccess(out bool isOwner);
        if (isOwner)
        {
            inZone = false;
            return;
        }

        if (flag)
        {
            if (!inZone) return;
            inZone = false;
            data.content += lastPrivateType == PrivateType.Ward ? "$LeftGuard" : "$LeftZone";
            data.username += lastPrivateType == PrivateType.Ward
                ? $"$Guard {lastPrivateName}"
                : $"$Zone {lastPrivateName}";
            Discord.SendMessage(data);
            return;
        }

        if (!flag)
        {
            if (inZone) return;
            inZone = true;
            data.content += lastPrivateType == PrivateType.Ward ? "$InGuard" : "$InZone";
            data.username += lastPrivateType == PrivateType.Ward
                ? $"$Guard {lastPrivateName}"
                : $"$Zone {lastPrivateName}";
            Discord.SendMessage(data);
            return;
        }
    }
}