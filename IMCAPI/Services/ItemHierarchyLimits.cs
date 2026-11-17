using System.Globalization;

namespace IMCAPI.Services;

/// <summary>
/// The one place that answers how deep the item tree may be. The hierarchy routes enforce it when
/// they create, move or restore a record, the importer enforces it before a row is written, and the
/// JSON serializer's depth is derived from it — a tree the API accepts has to be a tree the API can
/// answer with, otherwise the depth bound is only a promise to the writer.
/// </summary>
public static class ItemHierarchyLimits
{
    /// <summary>The environment variable a deployment uses to set the bound.</summary>
    public const string EnvironmentVariable = "ITEMS_MAX_DEPTH";

    /// <summary>What a deployment that does not say anything gets.</summary>
    public const int DefaultMaxDepth = 12;

    /// <summary>
    /// Reads the bound from the environment first, then the configuration, then the default — the
    /// order the routes have always used. Null configuration is a caller that has none (a test).
    /// </summary>
    public static int ResolveMaxDepth(IConfiguration? configuration)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (int.TryParse(fromEnvironment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var environmentDepth)
            && environmentDepth > 0)
        {
            return environmentDepth;
        }

        if (int.TryParse(configuration?["Items:MaxDepth"], NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var configuredDepth) && configuredDepth > 0)
        {
            return configuredDepth;
        }

        return DefaultMaxDepth;
    }

    /// <summary>
    /// The JSON container depth a legal tree of <paramref name="maxDepth"/> levels needs. Each level
    /// of the response is an item object plus its <c>children</c> array (two containers), and each
    /// node also nests its ancestors under <c>parent</c> (one more container per level up). Measured
    /// against a legal chain of that length; the constant covers the response envelope.
    /// </summary>
    public static int JsonDepthFor(int maxDepth) => maxDepth * 3 + 8;
}
