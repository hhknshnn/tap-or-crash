using System.Collections.Generic;
using UnityEngine.Purchasing;

// The one authoritative list of IAP product ids. Every script that needs a
// product id references these constants rather than typing the string again.
public static class MonetizationProducts
{
    public const string FuelFullRefill = "fuel_full_refill";
    public const string RocketCat = "rocket_cat";
    public const string RocketDog = "rocket_dog";
    public const string RocketRetroUfo = "rocket_retro_ufo";

    public static readonly IReadOnlyList<ProductDefinition> Catalog = new[]
    {
        new ProductDefinition(FuelFullRefill, ProductType.Consumable),
        new ProductDefinition(RocketCat, ProductType.NonConsumable),
        new ProductDefinition(RocketDog, ProductType.NonConsumable),
        new ProductDefinition(RocketRetroUfo, ProductType.NonConsumable),
    };

    public static bool IsRocketProduct(string productId) =>
        productId == RocketCat || productId == RocketDog || productId == RocketRetroUfo;

    public static bool IsKnownProduct(string productId) =>
        productId == FuelFullRefill || IsRocketProduct(productId);
}
