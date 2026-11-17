namespace LaunchGiftPortraits.Content;

/// <summary>Content classification stays here rather than in the generic runtime.</summary>
static class CustomerBeautyPolicy
{
    // These named narrative actors share the visitor pack for asset delivery only.
    static readonly HashSet<string> storyActors = new(StringComparer.Ordinal)
    {
        "admin", "cartel_handler", "courrier", "merchant", "sawyer",
        "waffle_enjoyer", "case_enjoyer"
    };

    public static bool ShouldReplace(string pack, string character) =>
        BeautyContentMod.Settings?.OrdinaryCustomerBeauty != false || pack != "visitors-v2"
        || (character != null && storyActors.Contains(character));
}
