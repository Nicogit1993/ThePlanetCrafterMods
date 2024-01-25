﻿using BepInEx;
using SpaceCraft;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using BepInEx.Configuration;
using System;
using UnityEngine.InputSystem;
using BepInEx.Logging;
using System.Linq;
using System.Reflection;

namespace UIHotbar
{
    [BepInPlugin(modUiHotbarGuid, "(UI) Hotbar", PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("akarnokd.theplanetcraftermods.uitranslationhungarian", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        const string modUiHotbarGuid = "akarnokd.theplanetcraftermods.uihotbar";

        static ConfigEntry<int> slotSize;
        static ConfigEntry<int> slotBottom;
        static ConfigEntry<int> fontSize;
        static ConfigEntry<bool> debugMode;

        static ManualLogSource _logger;

        static readonly int shadowContainerId = 4000;

        static Action<Group> pinUnpinRecipe;

        static AccessTools.FieldRef<CanvasPinedRecipes, List<Group>> fCanvasPinedRecipesGroupsAdded;
        static MethodInfo mCanvasPinedRecipesRemovePinnedRecipeAtIndex;

        void Awake()
        {
            LibCommon.BepInExLoggerFix.ApplyFix();

            // Plugin startup logic
            Logger.LogInfo($"Plugin is loaded!");

            slotSize = Config.Bind("General", "SlotSize", 75, "The size of each inventory slot");
            fontSize = Config.Bind("General", "FontSize", 20, "The font size of the slot index");
            slotBottom = Config.Bind("General", "SlotBottom", 40, "Placement of the panels relative to the bottom of the screen.");
            debugMode = Config.Bind("General", "DebugMode", false, "Enable debug mode logging? (Chatty!)");

            _logger = Logger;

            fCanvasPinedRecipesGroupsAdded = AccessTools.FieldRefAccess<CanvasPinedRecipes, List<Group>>("groupsAdded");
            mCanvasPinedRecipesRemovePinnedRecipeAtIndex = AccessTools.Method(typeof(CanvasPinedRecipes), "RemovePinnedRecipeAtIndex");
            
            pinUnpinRecipe = DefaultPinUnpinRecipe;

            // TODO if the (UI) Pin Recipe is installed, override pinUnpinRecipe...

            Harmony.CreateAndPatchAll(typeof(Plugin));
        }

        static void Log(object message)
        {
            if (debugMode.Value)
            {
                _logger.LogInfo(message);
            }
        }

        void Update()
        {
            PlayersManager playersManager = Managers.GetManager<PlayersManager>();
            if (playersManager != null)
            {
                PlayerMainController player = playersManager.GetActivePlayerController();
                if (player != null)
                {
                    Setup();
                    UpdateRender(player);
                    return;
                }
            }
            Teardown();
        }

        static GameObject parent;
        static readonly List<HotbarSlot> slots = [];
        static int slotCount = 9;
        static int activeSlot = -1;

        class HotbarSlot
        {
            internal GameObject background;
            internal GameObject image;
            internal GameObject number;
            internal GameObject buildCount;
            internal Group currentGroup;

            internal void Destroy()
            {
                UnityEngine.Object.Destroy(number);
                UnityEngine.Object.Destroy(image);
                UnityEngine.Object.Destroy(background);
                UnityEngine.Object.Destroy(buildCount);
                number = null;
                image = null;
                background = null;
                buildCount = null;
                currentGroup = null;
            }
        }

        static Color defaultBackgroundColor = new Color(0.25f, 0.25f, 0.25f, 0.8f);
        static Color defaultSlotNumberColor = new Color(1f, 1f, 1f, 1f);
        static Color defaultInvisibleColor = new Color(0f, 0f, 0f, 0f);
        static Color defaultHighlightColor = new Color(1f, 0.75f, 0f, 0.6f);
        static Color defaultImageColor = new Color(1f, 1f, 1f, 1f);
        static Color defaultImageColorDimmed = new Color(1f, 1f, 1f, 0.5f);
        static Color defaultCanCraftColor = new Color(0.5f, 1f, 0.5f, 1f);
        static Color defaultCannotCraftColor = new Color(1f, 0.5f, 0.5f, 1f);

        void Setup()
        {
            if (parent == null)
            {
                Log("Begin Creating the Hotbar");
                parent = new GameObject("HotbarCanvas");
                Canvas canvas = parent.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                int fs = fontSize.Value;
                int s = slotSize.Value;
                int x = -(slotCount * s + (slotCount - 1) * 5) / 2 + s / 2;
                int y = -Screen.height / 2 + slotBottom.Value + s / 2;
                slots.Clear();
                for (int i = 0; i < slotCount; i++)
                {
                    HotbarSlot slot = new HotbarSlot();
                    slots.Add(slot);

                    RectTransform rectTransform;
                    Image image;
                    Text text;

                    // -----------------------------------------------------------

                    slot.background = new GameObject();
                    slot.background.transform.parent = parent.transform;

                    image = slot.background.AddComponent<Image>();
                    image.color = defaultBackgroundColor;

                    rectTransform = image.GetComponent<RectTransform>();
                    rectTransform.localPosition = new Vector3(x, y, 0);
                    rectTransform.sizeDelta = new Vector2(s, s);

                    // -----------------------------------------------------------

                    slot.number = new GameObject();
                    slot.number.transform.parent = parent.transform;

                    text = slot.number.AddComponent<Text>();
                    text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    text.text = (i + 1).ToString();
                    text.color = defaultSlotNumberColor;
                    text.fontSize = fs;
                    text.resizeTextForBestFit = false;
                    text.verticalOverflow = VerticalWrapMode.Truncate;
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                    text.alignment = TextAnchor.UpperLeft;

                    rectTransform = text.GetComponent<RectTransform>();
                    rectTransform.localPosition = new Vector3(x, y, 0);
                    rectTransform.sizeDelta = new Vector2(s, s);

                    // -----------------------------------------------------------

                    slot.image = new GameObject();
                    slot.image.transform.parent = parent.transform;

                    image = slot.image.AddComponent<Image>();
                    image.color = defaultInvisibleColor;

                    rectTransform = image.GetComponent<RectTransform>();
                    rectTransform.localPosition = new Vector3(x + 2, y, 0);
                    rectTransform.sizeDelta = new Vector2(s, s);
                    slot.image.transform.localScale = new Vector3(1f, 1f, 1f);

                    // -----------------------------------------------------------

                    slot.buildCount = new GameObject();
                    slot.buildCount.transform.parent = parent.transform;

                    text = slot.buildCount.AddComponent<Text>();
                    text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    text.text = "";
                    text.color = defaultInvisibleColor;
                    text.fontSize = fs;
                    text.resizeTextForBestFit = false;
                    text.verticalOverflow = VerticalWrapMode.Truncate;
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                    text.alignment = TextAnchor.UpperRight;

                    rectTransform = text.GetComponent<RectTransform>();
                    rectTransform.localPosition = new Vector3(x - 2, y, 0);
                    rectTransform.sizeDelta = new Vector2(s, s);

                    // -----------------------------------------------------------
                    x += s + 5;
                }

                RestoreHotbar();
            }
        }

        void Teardown()
        {
            if (slots != null)
            {
                foreach (HotbarSlot slot in slots)
                {
                    slot.Destroy();
                }
            }
            slots.Clear();
            Destroy(parent);
            parent = null;
            activeSlot = -1;
        }

        void UpdateRender(PlayerMainController player)
        {
            bool isFreeCraft = Managers.GetManager<GameSettingsHandler>().GetCurrentGameSettings().GetFreeCraft();
            WindowsHandler wh = Managers.GetManager<WindowsHandler>();

            int oldActiveSlot = activeSlot;

            Dictionary<string, int> inventoryCounts = new Dictionary<string, int>();
            CountInventory(player, inventoryCounts);

            if (wh != null && !wh.GetHasUiOpen())
            {
                int k = WhichNumberKeyWasPressed();
                if (k != -1)
                {
                    if (Keyboard.current[Key.LeftShift].isPressed && pinUnpinRecipe != null 
                        && k < slots.Count && slots[k].currentGroup != null)
                    {
                        Group g = slots[k].currentGroup;
                        Log("Pin/Unpin Recipe for " + g.GetId());
                        pinUnpinRecipe.Invoke(g);
                    }
                    else
                    {

                        PlayerBuilder pb = player.GetPlayerBuilder();
                        if (activeSlot == -1 || activeSlot != k)
                        {
                            if (k < slots.Count && slots[k].currentGroup != null)
                            {
                                // cancel current ghost
                                if (pb.GetIsGhostExisting())
                                {
                                    Log("Cancelling previous ghost");
                                    pb.InputOnCancelAction();
                                }

                                GroupConstructible gc = (GroupConstructible)slots[k].currentGroup;

                                // activate build mode for slot k
                                if (isFreeCraft || BuildableCount(inventoryCounts, gc) > 0)
                                {
                                    Log("Activating ghost for " + gc.GetId());
                                    pb.SetNewGhost(gc);
                                }
                                else
                                {
                                    Managers.GetManager<BaseHudHandler>().DisplayCursorText("", 2f, "Not enough ingredients to craft " + Readable.GetGroupName(gc));
                                }
                            }
                        }
                        else
                        if (k == activeSlot)
                        {
                            activeSlot = -1;
                            // cancel current ghost
                            if (pb.GetIsGhostExisting())
                            {
                                Log("Cancelling current ghost");
                                pb.InputOnCancelAction();
                            }
                        }
                    }
                }
            }
            // Change highlights
            if (wh != null && wh.GetHasUiOpen())
            {
                activeSlot = -1;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                HotbarSlot slot = slots[i];

                if (activeSlot == i)
                {
                    slot.background.GetComponent<Image>().color = defaultHighlightColor;
                }
                else
                {
                    slot.background.GetComponent<Image>().color = defaultBackgroundColor;
                }

                if (slot.currentGroup != null)
                {
                    GroupConstructible gc = (GroupConstructible)slot.currentGroup;

                    int buildableCount = BuildableCount(inventoryCounts, gc);

                    Image image = slot.image.GetComponent<Image>();
                    Text text = slot.buildCount.GetComponent<Text>();

                    if (isFreeCraft || buildableCount > 0)
                    {
                        image.color = defaultImageColor;
                        text.color = defaultCanCraftColor;
                    }
                    else
                    {
                        image.color = defaultImageColorDimmed;
                        text.color = defaultCannotCraftColor;
                    }

                    text.text = buildableCount.ToString();
                }
            }
            if (activeSlot != -1 && activeSlot != oldActiveSlot)
            {
                player.GetMultitool().SetState(DataConfig.MultiToolState.Build);
            }
        }

        static void CountInventory(Inventory inv, Dictionary<string, int> counts)
        {
            foreach (WorldObject wo in inv.GetInsideWorldObjects())
            {
                string gid = wo.GetGroup().GetId();
                counts.TryGetValue(gid, out int c);
                counts[gid] = c + 1;
            }
        }

        static void CountInventory(PlayerMainController player, Dictionary<string, int> inventoryCounts)
        {
            CountInventory(player.GetPlayerBackpack().GetInventory(), inventoryCounts);
        }

        static int BuildableCount(Dictionary<string, int> inventoryCounts, GroupConstructible gc)
        {
            List<Group> recipe = gc.GetRecipe().GetIngredientsGroupInRecipe();
            // agregate recipe
            Dictionary<string, int> recipeCounts = new Dictionary<string, int>();
            foreach (Group group in recipe)
            {
                string gid = group.GetId();
                recipeCounts.TryGetValue(gid, out int c);
                recipeCounts[gid] = c + 1;
            }

            int craftableCount = int.MaxValue;
            foreach (Group comp in recipe)
            {
                inventoryCounts.TryGetValue(comp.GetId(), out int inventoryCount);
                recipeCounts.TryGetValue(comp.GetId(), out int recipeCount);

                craftableCount = Mathf.Min(craftableCount, inventoryCount / recipeCount);
            }
            if (craftableCount == int.MaxValue)
            {
                craftableCount = 0;
            }
            return craftableCount;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerBuilder), nameof(PlayerBuilder.SetNewGhost))]
        static bool PlayerBuilder_SetNewGhost(GroupConstructible groupConstructible, ConstructibleGhost ___ghost)
        {
            Log("New Ghost Set: " + groupConstructible?.GetId() ?? "null");
            Log("Previous Ghost: " + (___ghost != null ? "Exists" : "Doesn't Exist"));
            if (groupConstructible != null) {
                for (int i = 0; i < slots.Count; i++)
                {
                    HotbarSlot slot = slots[i];
                    if (slot.currentGroup != null && slot.currentGroup.GetId() == groupConstructible.GetId())
                    {
                        activeSlot = i;
                    }
                }
            } 
            else
            {
                activeSlot = -1;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerBuilder), nameof(PlayerBuilder.InputOnAction))]
        static bool PlayerBuilder_InputOnAction()
        {
            activeSlot = -1;
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerBuilder), nameof(PlayerBuilder.InputOnCancelAction))]
        static bool PlayerBuilder_InputOnCancelAction()
        {
            activeSlot = -1;
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerBuilder), nameof(PlayerBuilder.InputOnCancelAction))]
        static void PlayerBuilder_InputOnCancelAction_Post(ref ConstructibleGhost ___ghost)
        {
            // workaround for Unity's fake null problem
            ___ghost = null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UiWindowConstruction), "OnImageClicked")]
        static bool UiWindowConstruction_OnImageClicked(EventTriggerCallbackData eventTriggerCallbackData)
        {
            if (eventTriggerCallbackData.pointerEventData.button == PointerEventData.InputButton.Left)
            {
                int slot = WhichNumberKeyHeld();
                if (slot >= 0)
                {
                    PinUnpinGroup(eventTriggerCallbackData.group, slot);
                    SaveHotbar();
                    return false;
                }
            }
            return true;
        }

        static Key[] numberKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
        };

        static int WhichNumberKeyHeld()
        {
            for (int i = 0; i < numberKeys.Length; i++)
            {
                Key k = numberKeys[i];
                if (Keyboard.current[k].isPressed)
                {
                    return i;
                }
            }
            return -1;
        }
        static int WhichNumberKeyWasPressed()
        {
            for (int i = 0; i < numberKeys.Length; i++)
            {
                Key k = numberKeys[i];
                if (Keyboard.current[k].wasPressedThisFrame)
                {
                    return i;
                }
            }
            return -1;
        }

        static void PinUnpinGroup(Group group, int slot)
        {
            if (slot >= 0 && slot < slots.Count)
            {
                HotbarSlot hotbarSlot = slots[slot];
                Image image = hotbarSlot.image.GetComponent<Image>();
                if (hotbarSlot.currentGroup == null || hotbarSlot.currentGroup.GetId() != group.GetId())
                {
                    Log("Pinning to slot " + slot + " - " + group.GetId());
                    hotbarSlot.currentGroup = group;
                    image.sprite = group.GetImage();
                    image.color = new Color(1f, 1f, 1f, 1f);
                }
                else
                {
                    Log("Unpinning to slot " + slot);
                    hotbarSlot.currentGroup = null;
                    image.sprite = null;
                    image.color = new Color(0f, 0f, 0f, 0f);
                    hotbarSlot.buildCount.GetComponent<Text>().text = "";
                }
                // unpin the same group if pinned somewhere else
                for (int i = 0; i < slots.Count; i++)
                {
                    if (i != slot)
                    {
                        hotbarSlot = slots[i];
                        if (hotbarSlot.currentGroup != null && hotbarSlot.currentGroup.GetId() == group.GetId())
                        {
                            ClearSlot(i);
                        }
                    }
                }
            }
        }

        static void ClearSlot(int slotId)
        {
            var hotbarSlot = slots[slotId];
            Log("Unpinning to slot " + slotId);
            hotbarSlot.currentGroup = null;
            var image = hotbarSlot.image.GetComponent<Image>();
            image.sprite = null;
            image.color = new Color(0f, 0f, 0f, 0f);
            hotbarSlot.buildCount.GetComponent<Text>().text = "";
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(VisualsToggler), nameof(VisualsToggler.ToggleUi))]
        static void VisualsToggler_ToggleUi(List<GameObject> ___uisToHide)
        {
            bool active = ___uisToHide[0].activeSelf;
            parent?.SetActive(active);
        }

        static WorldObject EnsureHiddenContainer()
        {
            var wo = WorldObjectsHandler.Instance.GetWorldObjectViaId(shadowContainerId);
            if (wo == null)
            {
                wo = WorldObjectsHandler.Instance.CreateNewWorldObject(GroupsHandler.GetGroupViaId("Container2"), shadowContainerId);
                wo.SetText("");
            }
            wo.SetDontSaveMe(false);
            return wo;
        }

        static void SaveHotbar()
        {
            // FIXME Multiplayer

            var wo = EnsureHiddenContainer();
            var str = string.Join(",", slots.Select(slot =>
            {
                var g = slot.currentGroup;
                if (g == null)
                {
                    return "";
                }
                return g.id;
            }));
            wo.SetText(str);
        }

        static void RestoreHotbar()
        {
            // FIXME Multiplayer

            var wo = EnsureHiddenContainer();
            RestoreHotbarFromString(wo.GetText());
        }

        static void RestoreHotbarFromString(string s)
        {
            if (s == null || s.Length == 0)
            {
                for (int i = 0; i < slotCount; i++)
                {
                    ClearSlot(i);
                }
                return;
            }
            var current = s.Split(',');

            for (int i = 0; i < slotCount; i++)
            {
                ClearSlot(i);
                if (i < current.Length)
                {
                    var gid = current[i];
                    var g = GroupsHandler.GetGroupViaId(gid);
                    if (g != null)
                    {
                        PinUnpinGroup(g, i);
                    }
                }
            }
        }

        static void DefaultPinUnpinRecipe(Group g)
        {
            CanvasPinedRecipes canvasPinedRecipes = Managers.GetManager<CanvasPinedRecipes>();

            if (canvasPinedRecipes != null)
            {
                if (canvasPinedRecipes.GetIsPinPossible())
                {
                    var list = fCanvasPinedRecipesGroupsAdded(canvasPinedRecipes);

                    var idx = list.IndexOf(g);
                    if (idx < 0)
                    {
                        canvasPinedRecipes.AddPinedRecipe(g);
                    }
                    else
                    {
                        mCanvasPinedRecipesRemovePinnedRecipeAtIndex.Invoke(canvasPinedRecipes, [idx]);
                    }
                }
                else
                {
                    Managers.GetManager<BaseHudHandler>().DisplayCursorText("UIHotbar_MissingPinChip", 5f);
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Localization), "LoadLocalization")]
        static void Localization_LoadLocalization(
        Dictionary<string, Dictionary<string, string>> ___localizationDictionary)
        {
            if (___localizationDictionary.TryGetValue("hungarian", out var dict))
            {
                dict["UIHotbar_MissingPinChip"] = "Nem lehet rögzíteni a receptet! Kérlek szerelj fel egy <color=#FFFF00>Recept kitűzés Mikrocsipet</color>.";
            }
            if (___localizationDictionary.TryGetValue("english", out dict))
            {
                dict["UIHotbar_MissingPinChip"] = "Can't pin recipe! Please equip a <color=#FFFF00>Recipe Pinning Microchip</color> first.";
            }
        }

    }
}
