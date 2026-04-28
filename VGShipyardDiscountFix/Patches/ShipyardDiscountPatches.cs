using Behaviour.Unit;
using HarmonyLib;
using Source.Galaxy;
using Source.Galaxy.POI;
using Source.Player;
using Source.Util;

namespace VGShipyardDiscountFix.Patches;

// Vanilla defines ReputationLevelExtensions.GetShipyardDiscount (4/8/12/20% at
// Friendly/Respected/Distinguished/Exalted) but never calls it — the shipyard
// charges full sticker price regardless of standing. This patch wires it in.
//
// SpaceShip.totalCost (the getter on the prefab instance) is the single value
// that drives both the displayed price (Shipyard.Update sets creditPrice.text)
// and the actual charge (TryBuySelectedShip → BuyShip → RemoveCredits). One
// postfix on the getter therefore covers display, CanAfford check, confirm
// dialog text, and the final RemoveCredits call.
//
// Scoping is strict: we only modify the result when the ship is *currently
// listed for sale at the station the player is at*. The player's docked ship
// also has inShipYard=true while parked, so that flag alone over-scopes; the
// reliable discriminator is membership in station.shipyard.spaceShips.
//
// Faction choice mirrors vanilla's gating logic (Shipyard.FactionRequirementsMet
// at line ~84488 in the decomp): the station's faction is what gates ship
// access, so it's the natural faction to read rep from for the discount.
[HarmonyPatch(typeof(SpaceShip), nameof(SpaceShip.totalCost), MethodType.Getter)]
internal static class TotalCostPatch
{
    // One-shot diagnostic: log the first time this postfix runs against a
    // listed shipyard ship, so it's obvious from BepInEx/LogOutput.log whether
    // the patch is wired up and what rep/discount it computed. Avoids spamming
    // the log every frame the shipyard UI refreshes.
    private static bool _logged;

    [HarmonyPostfix]
    private static void Postfix(SpaceShip __instance, ref float __result)
    {
        var player = GamePlayer.current;
        if (player?.currentPointOfInterest is not SpaceStation station) return;
        if (station.shipyard == null || station.faction == null) return;

        // Only listed-for-sale ships get the discount. Scene instances carry
        // Unity's "(Clone)" suffix on .name; ShipyardShip stores the clean
        // prefab key. Strip the suffix before comparing.
        var instanceName = __instance.name;
        var cloneIdx = instanceName.IndexOf("(Clone)", System.StringComparison.Ordinal);
        if (cloneIdx >= 0) instanceName = instanceName.Substring(0, cloneIdx);

        var listed = false;
        foreach (var s in station.shipyard.spaceShips)
        {
            if (s.name == instanceName) { listed = true; break; }
        }
        if (!listed) return;

        var level = station.faction.GetReputationLevel(Faction.player);
        var discount = level.GetShipyardDiscount();

        if (!_logged)
        {
            _logged = true;
            Plugin.Log.LogInfo(
                $"Shipyard discount probe: station='{station.name}' " +
                $"faction='{station.faction.identifier}' rep={level} " +
                $"discount={discount:P0} ship='{__instance.name}' " +
                $"sticker={__result:F0}");
        }

        if (discount <= 0f) return;

        __result *= 1f - discount;
    }
}
