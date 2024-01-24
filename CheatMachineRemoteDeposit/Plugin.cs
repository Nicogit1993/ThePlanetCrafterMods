﻿using BepInEx;
using BepInEx.Configuration;
using SpaceCraft;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using System.Collections;
using UnityEngine;

namespace CheatMachineRemoteDeposit
{
    [BepInPlugin("akarnokd.theplanetcraftermods.cheatmachineremotedeposit", "(Cheat) Machines Deposit Into Remote Containers", PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(modCheatInventoryStackingGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        const string modCheatInventoryStackingGuid = "akarnokd.theplanetcraftermods.cheatinventorystacking";

        static ManualLogSource logger;

        static ConfigEntry<bool> modEnabled;
        static ConfigEntry<bool> debugMode;

        static readonly Dictionary<string, string> depositAliases = [];

        /// <summary>
        /// If the stacking mod is present, this will delegate to its apiIsFullStackedInventory call
        /// that considers the stackability of the target inventory with respect to the groupId to be added to i.
        /// </summary>
        static Func<Inventory, string, bool> InventoryCanAdd;

        /// <summary>
        /// If set, the <see cref="MachineGenerator_GenerateAnObject(List{GroupData}, bool, WorldObject, List{GroupData}, ref WorldUnitsHandler, TerraformStage)"/>
        /// won't be executed because the business logic has been taken care of by the stacking mod.
        /// </summary>
        static bool stackingOverridden;

        void Awake()
        {
            LibCommon.BepInExLoggerFix.ApplyFix();

            // Plugin startup logic
            Logger.LogInfo($"Plugin is loaded!");

            logger = Logger;

            modEnabled = Config.Bind("General", "Enabled", true, "Is the mod enabled?");
            debugMode = Config.Bind("General", "DebugMode", false, "Produce detailed logs? (chatty)");

            ProcessAliases(Config.Bind("General", "Aliases", "", "A comma separated list of resourceId:aliasForId, for example, Iron:A,Cobalt:B,Uranim:C"));

            InventoryCanAdd = (inv, gid) => !inv.IsFull();

            // If present, we interoperate with the stacking mod.
            // It has places to override the inventory into which items should be added in some machines.
            // However, when this mod looks for suitable inventories, we need to use stacking to correctly
            // determine if a stackable inventory can take an item.
            if (Chainloader.PluginInfos.TryGetValue(modCheatInventoryStackingGuid, out var info))
            {
                Logger.LogInfo("Mod " + modCheatInventoryStackingGuid + " found, overriding FindInventoryForGroupID.");

                var modType = info.Instance.GetType();

                // make sure stacking knowns when this mod is enabled before calling that callback
                AccessTools.Field(modType, "IsFindInventoryForGroupIDEnabled")
                    .SetValue(null, new Func<bool>(() => modEnabled.Value));
                // tell stacking to use this callback to find an inventory for a group id
                AccessTools.Field(modType, "FindInventoryForGroupID")
                    .SetValue(null, new Func<string, Inventory>(FindInventoryForOre));

                // get the function that can tell if an inventory can't take one item of a provided group id
                var apiIsFullStackedInventory = (Func<Inventory, string, bool>)AccessTools.Field(modType, "apiIsFullStackedInventory").GetValue(null);
                // we need to logically invert it as we need it as "can-do"
                InventoryCanAdd = (inv, gid) => !apiIsFullStackedInventory(inv, gid);

                stackingOverridden = true;
            }
            else
            {
                Logger.LogInfo("Mod " + modCheatInventoryStackingGuid + " not found.");
            }

            Harmony.CreateAndPatchAll(typeof(Plugin));
        }

        void ProcessAliases(ConfigEntry<string> cfe)
        {
            var s = cfe.Value.Trim();
            if (s.Length != 0)
            {
                var i = 0;
                foreach (var str in s.Split(','))
                {
                    var idalias = str.Split(':');
                    if (idalias.Length != 2)
                    {
                        Logger.LogWarning("Wrong alias @ index " + i + " value " + str);
                    }
                    else
                    {
                        depositAliases[idalias[0]] = idalias[1].ToLower();
                        log("Alias " + idalias[0] + " -> " + idalias[1]);
                    }
                    i++;
                }
            }
        }

        static void log(string s)
        {
            if (debugMode.Value)
            {
                logger.LogInfo(s);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MachineGenerator), "GenerateAnObject")]
        static bool MachineGenerator_GenerateAnObject(
            List<GroupData> ___groupDatas,
            bool ___setGroupsDataViaLinkedGroup,
            WorldObject ___worldObject,
            List<GroupData> ___groupDatasTerraStage,
            ref WorldUnitsHandler ___worldUnitsHandler,
            TerraformStage ___terraStage
        )
        {
            if (!modEnabled.Value || stackingOverridden)
            {
                return true;
            }

            log("GenerateAnObject start");

            if (___worldUnitsHandler == null)
            {
                ___worldUnitsHandler = Managers.GetManager<WorldUnitsHandler>();
            }
            if (___worldUnitsHandler == null)
            {
                return false;
            }

            log("    begin ore search");

            Group group = null;
            if (___groupDatas.Count != 0)
            {
                List<GroupData> list = new(___groupDatas);
                if (___groupDatasTerraStage.Count != 0 && ___worldUnitsHandler.IsWorldValuesAreBetweenStages(___terraStage, null))
                {
                    list.AddRange(___groupDatasTerraStage);
                }
                group = GroupsHandler.GetGroupViaId(list[UnityEngine.Random.Range(0, list.Count)].id);
            }
            if (___setGroupsDataViaLinkedGroup)
            {
                if (___worldObject.GetLinkedGroups() != null && ___worldObject.GetLinkedGroups().Count > 0)
                {
                    group = ___worldObject.GetLinkedGroups()[UnityEngine.Random.Range(0, ___worldObject.GetLinkedGroups().Count)];
                }
                else
                {
                    group = null;
                }
            }

            // deposit the ore

            if (group != null)
            {
                string oreId = group.id;

                log("    ore: " + oreId);

                var inventory = FindInventoryForOre(oreId);                

                if (inventory != null)
                {
                    InventoriesHandler.Instance.AddItemToInventory(group, inventory, (success, id) =>
                    {
                        if (!success)
                        {
                            log("GenerateAnObject: Machine " + ___worldObject.GetId() + " could not add " + oreId + " to inventory " + inventory.GetId());
                            if (id != 0)
                            {
                                WorldObjectsHandler.Instance.DestroyWorldObject(id);
                            }
                        }
                    });
                }
                else
                {
                    log("    No suitable inventory found, ore ignored");
                }
            }
            else
            {
                log("    ore: none");
            }

            log("GenerateAnObject end");
            return false;
        }

        /// <summary>
        /// When the vanilla sets up a Machine Generator, we have to launch
        /// the inventory cleaning routine to unclog it.
        /// Otherwise a full inventory will stop the ore generation in the
        /// MachineGenerator.TryToGenerate method.
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="_inventory"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MachineGenerator), nameof(MachineGenerator.SetGeneratorInventory))]
        static void MachineGenerator_SetGeneratorInventory(
            MachineGenerator __instance, 
            Inventory _inventory)
        {
            __instance.StartCoroutine(ClearMachineGeneratorInventory(_inventory, __instance.spawnEveryXSec));
        }

        /// <summary>
        /// When the speed multiplier is changed, the vanilla code
        /// cancels all coroutines and starts a new generation coroutine.
        /// We have to also restart our inventory cleaning routine.
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="___inventory"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MachineGenerator), nameof(MachineGenerator.AddToGenerationSpeedMultiplier))]
        static void MachineGenerator_AddToGenerationSpeedMultiplier(
            MachineGenerator __instance,
            Inventory ___inventory
        )
        {
            __instance.StartCoroutine(ClearMachineGeneratorInventory(___inventory, __instance.spawnEveryXSec));
        }

        static IEnumerator ClearMachineGeneratorInventory(Inventory _inventory, int delay)
        {
            var wait = new WaitForSeconds(delay);
            while (true)
            {
                // Server side is responsible for the transfer.
                if (InventoriesHandler.Instance != null && InventoriesHandler.Instance.IsServer)
                {
                    log("ClearMachineGeneratorInventory begin");
                    var items = _inventory.GetInsideWorldObjects();

                    for (int i = items.Count - 1; i >= 0; i--)
                    {
                        var item = items[i];
                        var oreId = item.GetGroup().GetId();
                        var candidateInv = FindInventoryForOre(oreId);
                        if (candidateInv != null)
                        {
                            log("    Transfer of " + item.GetId() + "(" + item.GetGroup().GetId() + ") from " + _inventory.GetId() + " to " + candidateInv.GetId());
                            InventoriesHandler.Instance.TransferItem(_inventory, candidateInv, item);
                        }
                    }
                    log("ClearMachineGeneratorInventory end");
                }
                yield return wait;
            }
        }



        static Inventory FindInventoryForOre(string oreId)
        {
            var containerNameFilter = "*" + oreId.ToLower();
            if (depositAliases.TryGetValue(oreId, out var alias))
            {
                containerNameFilter = alias;
            }

            foreach (var constructs in WorldObjectsHandler.Instance.GetConstructedWorldObjects())
            {
                if (constructs != null && constructs.HasLinkedInventory())
                {
                    string txt = constructs.GetText();
                    if (txt != null && txt.ToLower().Contains(containerNameFilter))
                    {
                        Inventory candidateInventory = InventoriesHandler.Instance.GetInventoryById(constructs.GetLinkedInventoryId());
                        if (candidateInventory != null && InventoryCanAdd(candidateInventory, oreId))
                        {
                            log("    Found Inventory: " + candidateInventory.GetId());
                            break;
                        }
                        else
                        {
                            log("    This inventory is full: " + txt);
                        }
                    }
                }
            }
            return null;
        }
    }
}
