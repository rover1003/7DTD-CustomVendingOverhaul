using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CustomVendingOverhaul
{
    public class CustomVendingOverhaulMod : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            Log.Out("[CustomVendingOverhaul] Initializing Custom Vending Overhaul Mod...");
            var harmony = new Harmony("CustomVendingOverhaul");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Out("[CustomVendingOverhaul] Harmony patches successfully applied!");
        }
    }

    /// <summary>
    /// Patch 1: Unlock unlimited rented vending machines.
    /// Allows the player to rent multiple vending machines across traders without restriction.
    /// </summary>
    [HarmonyPatch(typeof(TileEntityVendingMachine), "checkAlreadyRentingVM")]
    public static class CheckAlreadyRentingVMPatch
    {
        public static bool Prefix(ref bool __result)
        {
            __result = false; // Never block player from renting another machine
            return false;     // Skip original check
        }
    }

    /// <summary>
    /// Patch 2: Overhaul TryAutoBuy logic (vanilla only calls this from XUiC_TraderWindow.OnOpen
    /// for RENTABLE machines owned by the player):
    /// - 100% daily purchase rate (no 33% failure chance, no pity accumulator).
    /// - Buys 3 to 5 stacks per sale (or all eligible if fewer than 3).
    /// - Removes the "must have > 5 items" restriction.
    /// - Credits Casino Coins to AvailableMoney for collection.
    /// - Skips price-gouged items (Markup > 0).
    /// - The daily timer is ALWAYS enforced (vanilla semantics): one sale batch per game day max.
    /// - After a sale the timer is re-anchored to now + 1 day, so re-opening the UI the same
    ///   day never sells again, and being away several days still yields only one batch.
    /// </summary>
    [HarmonyPatch(typeof(TileEntityVendingMachine), "TryAutoBuy")]
    public static class VendingMachineTryAutoBuyPatch
    {
        private const ulong TicksPerDay = 24000UL;

        public static bool Prefix(TileEntityVendingMachine __instance, ref bool __result)
        {
            __result = ExecuteAutoBuy(__instance);
            return false; // Skip original method
        }

        private static bool ExecuteAutoBuy(TileEntityVendingMachine vm)
        {
            var world = GameManager.Instance?.World;
            if (world == null) return false;

            var traderData = vm.TraderData;
            if (traderData?.PrimaryInventory == null) return false;

            // Daily timer gate - always enforced. A nextAutoBuy of 0 (never initialized,
            // e.g. player-bought machines) counts as elapsed so the first open sells.
            if (vm.nextAutoBuy != 0UL && world.worldTime <= vm.nextAutoBuy)
            {
                return false;
            }

            var inventory = traderData.PrimaryInventory;
            if (inventory.Count == 0) return false;

            // 1. Find all eligible candidate items (Markup <= 0, valid sellable items)
            var eligibleIndices = new List<int>();
            for (int i = 0; i < inventory.Count; i++)
            {
                var entry = inventory[i];
                if (entry == null) continue;
                if (entry.Markup > 0) continue; // Skip price-gouged items

                var itemStack = entry.Item;
                if (itemStack == null || itemStack.IsEmpty()) continue;

                var itemClass = itemStack.itemValue?.ItemClass;
                if (itemClass == null || !itemClass.SellableToTrader) continue;

                float econVal = itemClass.IsBlock()
                    ? Block.list[itemStack.itemValue.type].EconomicValue
                    : itemClass.EconomicValue;

                if (econVal > 0f)
                {
                    eligibleIndices.Add(i);
                }
            }

            if (eligibleIndices.Count == 0) return false;

            // 2. Determine target purchase count: 3 to 5 stacks (or all eligible if fewer)
            var rand = GameManager.Instance?.lootManager?.Random ?? world.GetGameRandom();
            int targetBuyCount = rand.RandomRange(3, 6); // 3, 4, or 5
            int actualBuyCount = Math.Min(targetBuyCount, eligibleIndices.Count);

            // Shuffle eligible indices to randomly pick distinct stacks
            for (int i = eligibleIndices.Count - 1; i > 0; i--)
            {
                int swapIdx = rand.RandomRange(0, i + 1);
                int temp = eligibleIndices[i];
                eligibleIndices[i] = eligibleIndices[swapIdx];
                eligibleIndices[swapIdx] = temp;
            }

            // Pick the target indices and sort descending so RemoveAt doesn't shift remaining indices
            var selectedIndices = eligibleIndices.GetRange(0, actualBuyCount);
            selectedIndices.Sort((a, b) => b.CompareTo(a)); // Descending order

            var xui = LocalPlayerUI.GetUIForPrimaryPlayer()?.xui;
            int totalDukesEarned = 0;
            int soldStacksCount = 0;

            foreach (int slotIdx in selectedIndices)
            {
                if (slotIdx >= inventory.Count) continue;

                var entry = inventory[slotIdx];
                var stack = entry.Item;
                if (stack == null || stack.IsEmpty()) continue;

                // Calculate price
                int stackPrice = 0;
                try
                {
                    if (xui != null)
                    {
                        stackPrice = XUiM_Trader.GetBuyPrice(xui, stack.itemValue, stack.count, null, slotIdx);
                    }
                }
                catch { }

                // Fallback price if UI calculation fails
                if (stackPrice <= 0)
                {
                    var itemClass = stack.itemValue.ItemClass;
                    float baseVal = itemClass.IsBlock()
                        ? Block.list[stack.itemValue.type].EconomicValue
                        : itemClass.EconomicValue;

                    float markupMult = 1f + (entry.Markup * 0.2f);
                    stackPrice = Mathf.Max(1, Mathf.RoundToInt(baseVal * stack.count * markupMult));
                }

                string itemName = stack.itemValue.ItemClass?.GetLocalizedItemName() ?? "Unknown";
                int count = stack.count;

                // Remove item and credit money
                inventory.RemoveAt(slotIdx);
                traderData.AvailableMoney += stackPrice;
                totalDukesEarned += stackPrice;
                soldStacksCount++;

                Log.Out($"[CustomVendingOverhaul] Auto-sold: {count}x {itemName} for {stackPrice} Dukes.");
            }

            if (soldStacksCount > 0)
            {
                // Re-anchor the daily timer to now + 1 game day so repeated UI opens
                // the same day never sell again, regardless of how long the machine sat.
                vm.SetAutoBuyTime(true);
                vm.SetModified();
                Log.Out($"[CustomVendingOverhaul] Daily Vending Summary at {vm.ToWorldPos()}: Sold {soldStacksCount} item stacks. Total earned: {totalDukesEarned} Dukes. Current balance: {traderData.AvailableMoney} Dukes.");
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Patch 3: Enable auto-buy for player-BOUGHT vending machines (placed at home).
    /// Vanilla XUiC_TraderWindow.OnOpen only triggers TryAutoBuy for RENTABLE machines;
    /// bought machines (TraderInfo.Rentable == false) are skipped entirely and never sell.
    /// This postfix applies the same on-open catch-up sale to owned non-rentable machines.
    /// Rented machines are left to the vanilla path to avoid double sales.
    /// </summary>
    [HarmonyPatch(typeof(XUiC_TraderWindow), "OnOpen")]
    public static class TraderWindowOnOpenPatch
    {
        public static void Postfix(XUiC_TraderWindow __instance)
        {
            var vending = __instance.CurrentVending;
            if (vending == null) return;
            if (vending.TraderData?.TraderInfo == null) return;
            if (vending.TraderData.TraderInfo.Rentable) return; // Vanilla already handles rented machines
            if (!vending.LocalPlayerIsOwner()) return;

            if (vending.TryAutoBuy(true))
            {
                __instance.RefreshTraderItems();
                __instance.Refresh();
            }
        }
    }
}
