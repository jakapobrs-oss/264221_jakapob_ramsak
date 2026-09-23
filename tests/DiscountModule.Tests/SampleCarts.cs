using DiscountModule.Models;

namespace DiscountModule.Tests;

/// <summary>Items and carts from the worked examples of the assignment.</summary>
internal static class SampleCarts
{
    public static CartItem TShirt => new("T-Shirt", ItemCategory.Clothing, 350m);
    public static CartItem Hat => new("Hat", ItemCategory.Accessories, 250m);
    public static CartItem Belt => new("Belt", ItemCategory.Accessories, 230m);
    public static CartItem Hoodie => new("Hoodie", ItemCategory.Clothing, 700m);
    public static CartItem Watch => new("Watch", ItemCategory.Electronics, 850m);
    public static CartItem Bag => new("Bag", ItemCategory.Accessories, 640m);

    /// <summary>T-Shirt 350 + Hat 250 = 600 THB.</summary>
    public static Cart TShirtAndHat() => new([TShirt, Hat]);

    /// <summary>T-Shirt 350 + Hat 250 + Belt 230 = 830 THB.</summary>
    public static Cart TShirtHatAndBelt() => new([TShirt, Hat, Belt]);

    /// <summary>T-Shirt 350 + Hoodie 700 (Clothing 1,050), Watch 850, Bag 640 = 2,540 THB.</summary>
    public static Cart MixedCategories() => new([TShirt, Hoodie, Watch, Bag]);

    /// <summary>A single line with the given price, for rule tests where the items do not matter.</summary>
    public static Cart SingleItem(decimal unitPrice) => new([new CartItem("Item", ItemCategory.Accessories, unitPrice)]);
}
