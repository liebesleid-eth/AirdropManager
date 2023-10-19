﻿using HarmonyLib;
using RestoreMonarchy.AirdropManager.Models;
using RestoreMonarchy.AirdropManager.Utilities;
using Rocket.API.Collections;
using Rocket.Core.Assets;
using Rocket.Core.Plugins;
using Rocket.Core.Utils;
using Rocket.Unturned.Chat;
using SDG.Unturned;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Timers;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace RestoreMonarchy.AirdropManager
{
    public class AirdropManagerPlugin : RocketPlugin<AirdropManagerConfiguration>
    {
        public static AirdropManagerPlugin Instance { get; set; }
        public Timer AirdropTimer { get; set; }
        public DateTime AirdropTimerNext { get; set; }
        public Color MessageColor { get; set; }                
        
        public FieldInfo LevelManagerAirdropNodesField { get; set; }
        public FieldInfo LevelManagerHasAirdropField { get; set; }
        
        public FieldInfo SpawnAssetRootsField { get; set; }
        public FieldInfo SpawnAssetTablesField { get; set; }
        public PropertyInfo SpawnAssetAreTablesDirtyProperty { get; set; }
        public PropertyInfo SpawnsAssetInsertRootsProperty { get; set; }
        public MethodInfo AddToMappingMethod { get; set; }
        public FieldInfo CurrentAssetMappingField { get; set; }
        public FieldInfo SpawnTableLegacyAssetIdField { get; set; }

        public override TranslationList DefaultTranslations =>  new TranslationList()
        {
            { "NextAirdrop", "Next airdrop will be in {0}" },
            { "SuccessAirdrop", "Successfully called in airdrop!" },
            { "SuccessMassAirdrop", "Successfully called in mass airdrop!" },            
            { "Airdrop", "<size=17>Airdrop is coming!</size>" },
            { "MassAirdrop", "<size=20>Mass Airdrop is coming!</size>" },
            { "SetAirdropFormat", "Format: /setairdrop <AirdropID>" },
            { "SetAirdropSuccess", "Successfully set an airdrop spawn at your position!" },
            { "SetAirdropInvalid", "You must specify AirdropID and optionally spawn name" },
            { "AirdropWithName", "<size=17>Airdrop will be dropped at {0}!</size>" }
        };

        public const string HarmonyId = "com.restoremonarchy.airdropmanager";

        private Harmony harmony;
        protected override void Load()
        {
            Instance = this;
            MessageColor = UnturnedChat.GetColorFromName(Configuration.Instance.MessageColor, Color.green);

            harmony = new Harmony(HarmonyId);
            harmony.PatchAll();
                        
            LevelManagerAirdropNodesField = typeof(LevelManager).GetField("airdropNodes", BindingFlags.Static | BindingFlags.NonPublic);
            LevelManagerHasAirdropField = typeof(LevelManager).GetField("_hasAirdrop", BindingFlags.Static | BindingFlags.NonPublic);
            
            SpawnAssetRootsField = typeof(SpawnAsset).GetField("_roots", BindingFlags.NonPublic | BindingFlags.Instance);
            SpawnAssetTablesField = typeof(SpawnAsset).GetField("_tables", BindingFlags.NonPublic | BindingFlags.Instance);
            SpawnAssetAreTablesDirtyProperty = typeof(SpawnAsset).GetProperty("areTablesDirty", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            SpawnsAssetInsertRootsProperty = typeof(SpawnAsset).GetProperty("insertRoots", BindingFlags.Instance | BindingFlags.Public);
            AddToMappingMethod = typeof(Assets).GetMethod("AddToMapping", BindingFlags.NonPublic | BindingFlags.Static);
            CurrentAssetMappingField = typeof(Assets).GetField("currentAssetMapping", BindingFlags.NonPublic | BindingFlags.Static);
            SpawnTableLegacyAssetIdField = typeof(SpawnTable).GetField("legacyAssetId", BindingFlags.NonPublic | BindingFlags.Instance);

            if (Level.isLoaded)
            {
                LoadAirdropSpawns(0);
                InitializeTimer(0);
                LoadAirdropAssets(0);
            } else
            {
                Level.onLevelLoaded += LoadAirdropSpawns;
                Level.onLevelLoaded += InitializeTimer;
                Level.onLevelLoaded += LoadAirdropAssets;
            }

            if (Provider.modeConfigData.Events.Use_Airdrops)
            {
                Provider.modeConfigData.Events.Use_Airdrops = false;
                Logger.Log("Automatically disabled Use_Airdrops in the Config.json", ConsoleColor.Yellow);
            }

            Logger.Log($"{Name} {Assembly.GetName().Version} has been loaded!", ConsoleColor.Yellow);
            Logger.Log($"Brought to You by RestoreMonarchy.com", ConsoleColor.Yellow);
        }

        protected override void Unload()
        {
            Level.onLevelLoaded -= LoadAirdropSpawns;
            Level.onLevelLoaded -= InitializeTimer;
            AirdropTimer.Elapsed -= AirdropTimer_Elapsed;

            Logger.Log($"{Name} has been unloaded!", ConsoleColor.Yellow);
        }

        public List<AirdropSpawn> AirdropSpawns { get; set; }

        private void InitializeTimer(int level)
        {
            AirdropTimer = new Timer(Configuration.Instance.AirdropInterval * 1000);
            AirdropTimer.Elapsed += AirdropTimer_Elapsed;
            AirdropTimer.AutoReset = true;
            AirdropTimer.Start();
            AirdropTimerNext = DateTime.Now.AddSeconds(Configuration.Instance.AirdropInterval);

            Logger.Log("Airdrop timer has been started!", ConsoleColor.Yellow);
        }

        private ushort GetRandomCustomAirdropId(ushort defaultValue)
        {
            int airdropsCount = Configuration.Instance.Airdrops.Count;

            if (airdropsCount == 0)
            {
                return defaultValue;
            }

            int randomIndex = UnityEngine.Random.Range(0, airdropsCount);
            Airdrop airdrop = Configuration.Instance.Airdrops[randomIndex];

            return airdrop.AirdropId;
        }

        private float GetAirdropSpeed()
        {
            return Configuration.Instance.AirdropSpeed ?? Provider.modeConfigData.Events.Airdrop_Speed;
        }

        private void LoadAirdropAssets(int level)
        {
            Logger.Log("Loading airdrop assets...", ConsoleColor.Yellow);
            foreach (Airdrop airdrop in Configuration.Instance.Airdrops)
            {
                if (Configuration.Instance.BlacklistedAirdrops.Contains(airdrop.AirdropId))
                    continue;

                SpawnAsset asset = new() 
                { 
                    id = airdrop.AirdropId
                };

                SpawnsAssetInsertRootsProperty.SetValue(asset, new List<SpawnTable>());
                SpawnAssetRootsField.SetValue(asset, new List<SpawnTable>());
                SpawnAssetTablesField.SetValue(asset, new List<SpawnTable>());

                foreach (AirdropItem item in airdrop.Items)
                {
                    SpawnTable spawnTable = new()
                    {
                        weight = item.Chance
                    };
                    SpawnTableLegacyAssetIdField.SetValue(spawnTable, item.ItemId);

                    asset.tables.Add(spawnTable);
                }

                SpawnAssetAreTablesDirtyProperty.SetValue(asset, true);

                object assetMapping = CurrentAssetMappingField.GetValue(null);
                AddToMappingMethod.Invoke(null, new object[] { asset, true, assetMapping });
            }
            Assets.linkSpawns();
            Logger.Log($"{Configuration.Instance.Airdrops.Count} airdrop assets have been loaded!", ConsoleColor.Yellow);
        }

        private void LoadAirdropSpawns(int level)
        {
            AirdropSpawns = new List<AirdropSpawn>();

            int defaultAirdropSpawnsCount = 0;
            int customAirdropSpawnsCount = 0;

            foreach (CustomAirdropSpawn customAirdropSpawn in Configuration.Instance.AirdropSpawns)
            {
                AirdropSpawn airdropSpawn = customAirdropSpawn.ToAirdropSpawn();

                AirdropSpawns.Add(airdropSpawn);
                customAirdropSpawnsCount++;
            }

            Logger.Log($"{customAirdropSpawnsCount} custom airdrop spawns have been loaded!");

            if (Configuration.Instance.UseDefaultSpawns)
            {
                List<AirdropDevkitNode> defaultAirdrops = LevelManagerAirdropNodesField.GetValue(null) as List<AirdropDevkitNode>;
                if (defaultAirdrops == null || defaultAirdrops.Count == 0)
                {
                    Logger.LogWarning("There isn't any default airdrop spawns on this server. You should disable UseDefaultSpawns in the config");
                } else
                {
                    foreach (AirdropDevkitNode defaultAirdrop in defaultAirdrops)
                    {
                        AirdropSpawn airdropSpawn = new()
                        {
                            AirdropId = defaultAirdrop.id,
                            Name = null,
                            Position = defaultAirdrop.transform.position,
                            IsDefault = true
                        };

                        if (!Configuration.Instance.UseDefaultAirdrops)
                        {
                            airdropSpawn.AirdropId = GetRandomCustomAirdropId(defaultAirdrop.id);
                        }

                        AirdropSpawns.Add(airdropSpawn);
                        defaultAirdropSpawnsCount++;
                    }
                    Logger.Log($"{defaultAirdropSpawnsCount} default airdrop spawns have been loaded!");
                }
            }
        }

        private void AirdropTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // Make sure it's executed on the main thread
            TaskDispatcher.QueueOnMainThread(() => 
            {
                CallAirdrop(false, false);
                AirdropTimerNext = DateTime.Now.AddSeconds(Configuration.Instance.AirdropInterval);
                Logger.Log("Airdrop has been called by a timer!", ConsoleColor.Yellow);
            });
        }

        public void CallMassAirdrop(bool shouldLog = true)
        {
            float airdropSpeed = GetAirdropSpeed();

            foreach (AirdropSpawn airdropSpawn in AirdropSpawns)
            {
                LevelManager.airdrop(airdropSpawn.Position, airdropSpawn.AirdropId, airdropSpeed);
            }

            ChatManager.serverSendMessage(Translate("MassAirdrop").ToRich(), MessageColor, null, null, EChatMode.SAY, Configuration.Instance.AirdropMessageIcon, true);
            
            if (shouldLog)
            {
                Logger.Log("Mass airdrop has been called!", ConsoleColor.Yellow);
            }            
        }

        public void CallAirdrop(bool isMass = false, bool shouldLog = true)
        {
            if (AirdropSpawns.Count == 0)
            {
                Logger.LogWarning("There isn't any airdrop spawns on this map or in the config. Use /setairdrop command to set custom spawns!");
                return;
            }

            if (isMass)
            {
                CallMassAirdrop();
                return;
            }

            AirdropSpawn airdropSpawn = AirdropSpawns[UnityEngine.Random.Range(0, AirdropSpawns.Count)];
            float airdropSpeed = GetAirdropSpeed();

            LevelManager.airdrop(airdropSpawn.Position, airdropSpawn.AirdropId, airdropSpeed);

            if (string.IsNullOrEmpty(airdropSpawn.Name)) 
            {
                ChatManager.serverSendMessage(Translate("Airdrop").ToRich(), MessageColor, null, null, EChatMode.SAY, Configuration.Instance.AirdropMessageIcon, true);
            } else
            {
                ChatManager.serverSendMessage(Translate("AirdropWithName", airdropSpawn.Name), MessageColor, null, null, EChatMode.SAY, Configuration.Instance.AirdropMessageIcon, true);
            }

            if (shouldLog)
            {
                Logger.Log("Airdrop has been called!", ConsoleColor.Yellow);
            }            
        }
    }
}