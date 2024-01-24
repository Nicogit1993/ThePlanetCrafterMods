﻿using HarmonyLib;
using SpaceCraft;
using System;
using System.Collections.Generic;
using System.Text;

namespace CheatInventoryStacking
{
    public partial class Plugin
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MachineGenerator), "GenerateAnObject")]
        static bool MachineGenerator_GenerateAnObject(
            Inventory ___inventory,
            List<GroupData> ___groupDatas,
            bool ___setGroupsDataViaLinkedGroup,
            WorldObject ___worldObject,
            List<GroupData> ___groupDatasTerraStage,
            ref WorldUnitsHandler ___worldUnitsHandler,
            TerraformStage ___terraStage
        )
        {
            if (stackSize.Value > 1)
            {
                // TODO these below are mostly duplicated within (Cheat) Machine Deposit Into Remote Containers
                //      eventually it would be great to get it factored out in some fashion...
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

                    var inventory = ___inventory;
                    if ((IsFindInventoryForGroupIDEnabled?.Invoke() ?? false) && FindInventoryForGroupID != null)
                    {
                        inventory = FindInventoryForGroupID(oreId);
                    }

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
            return true;
        }

        /// <summary>
        /// Conditionally disallow stacking in Ore Extractors, Water and Atmosphere generators.
        /// </summary>
        /// <param name="__instance">The current component used to find the world object's group id</param>
        /// <param name="_inventory">The inventory of the machine being set.</param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MachineGenerator), nameof(MachineGenerator.SetGeneratorInventory))]
        static void MachineGenerator_SetGeneratorInventory(MachineGenerator __instance, Inventory _inventory)
        {
            var wo = __instance.GetComponent<WorldObjectAssociated>()?.GetWorldObject();
            if (wo != null)
            {
                var gid = wo.GetGroup().id;
                if (gid.StartsWith("OreExtractor"))
                {
                    if (!stackOreExtractors.Value)
                    {
                        _noStackingInventories.Add(_inventory.GetId());
                    }
                }
                else if (gid.StartsWith("WaterCollector"))
                {
                    if (!stackWaterCollectors.Value)
                    {
                        _noStackingInventories.Add(_inventory.GetId());
                    }
                }
                else if (gid.StartsWith("GasExtractor"))
                {
                    if (!stackGasExtractors.Value)
                    {
                        _noStackingInventories.Add(_inventory.GetId());
                    }
                }
                else if (gid.StartsWith("Beehive"))
                {
                    if (!stackBeehives.Value)
                    {
                        _noStackingInventories.Add(_inventory.GetId());
                    }
                }
                else if (gid.StartsWith("Biodome"))
                {
                    if (!stackBiodomes.Value)
                    {
                        _noStackingInventories.Add(_inventory.GetId());
                    }
                }
            }
        }
    }
}
