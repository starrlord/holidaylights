namespace HolidayLights.Rendering.Layers;

/// <summary>The fallback chain of PRODUCT-SPEC 5.1: (a) behind the icons, then (b) in front of the icons, then (c) on top.</summary>
internal static class LayerModeChain
{
    private static readonly LayerMode[] FromBehind = [LayerMode.BehindIcons, LayerMode.InFrontOfIcons, LayerMode.OnTop];
    private static readonly LayerMode[] FromInFront = [LayerMode.InFrontOfIcons, LayerMode.OnTop];
    private static readonly LayerMode[] OnTopOnly = [LayerMode.OnTop];

    /// <summary>The modes to try for a requested mode, best first.</summary>
    public static IReadOnlyList<LayerMode> For(LayerMode requested) => requested switch
    {
        LayerMode.BehindIcons => FromBehind,
        LayerMode.InFrontOfIcons => FromInFront,
        _ => OnTopOnly,
    };

    /// <summary>
    /// The modes to try now. Without a shell (Explorer restarting) only the requested mode is tried until the back-off has
    /// waited long enough, so the lights do not jump on top of every window for the second Explorer needs to come back.
    /// </summary>
    /// <param name="requested">The requested mode.</param>
    /// <param name="shellRunning">Progman exists.</param>
    /// <param name="allowFallbackWithoutShell">The restart back-off is exhausted (or no restart is in progress).</param>
    public static IReadOnlyList<LayerMode> Candidates(LayerMode requested, bool shellRunning, bool allowFallbackWithoutShell) =>
        shellRunning || allowFallbackWithoutShell || requested == LayerMode.OnTop ? For(requested) : [requested];

    /// <summary>True when <paramref name="candidate"/> is closer to the requested mode than <paramref name="current"/>.</summary>
    public static bool IsBetter(LayerMode candidate, LayerMode current, LayerMode requested)
    {
        IReadOnlyList<LayerMode> chain = For(requested);
        int candidateRank = IndexOf(chain, candidate);
        int currentRank = IndexOf(chain, current);
        return candidateRank >= 0 && (currentRank < 0 || candidateRank < currentRank);
    }

    private static int IndexOf(IReadOnlyList<LayerMode> chain, LayerMode mode)
    {
        for (int i = 0; i < chain.Count; i++)
        {
            if (chain[i] == mode)
            {
                return i;
            }
        }

        return -1;
    }
}
