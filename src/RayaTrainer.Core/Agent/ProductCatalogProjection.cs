using System.Diagnostics.CodeAnalysis;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// One typed parameter slot a product expects, projected from the generated catalog's
/// <see cref="GeneratedProductParameterDescriptor"/> onto the Core.Agent surface so managed
/// consumers know how many/what typed values to collect before submitting an intent.
/// </summary>
public readonly record struct ProductCatalogParameter(string Name, ScriptValueKind Kind);

/// <summary>
/// A product from <see cref="GeneratedProductDefinitionCatalog"/> projected onto the
/// Core.Agent submit shape. The managed surfaces (WPF U2 / Web U4) enumerate these and derive
/// each intent's <see cref="ContextBinding"/> and parameters from the product's DECLARED
/// definition instead of hardcoding <c>(Live, CurrentPlayer, None)</c>.
/// </summary>
public sealed record ProductCatalogEntry(
    ProductId ProductId,
    string DisplayName,
    GeneratedProductAvailability Availability,
    GeneratedProductKind Kind,
    ScopeKind Scope,
    BindingKind Binding,
    ReapplyPolicy Reapply,
    IReadOnlyList<ProductCatalogParameter> Parameters)
{
    /// <summary>The <see cref="ContextBinding"/> to submit for this product.</summary>
    public ContextBinding ToContextBinding() => new(Binding, Scope, Reapply);
}

/// <summary>
/// Projects the generated product-definition catalog onto the Core.Agent submit shape. The
/// generated <see cref="GeneratedProductScope"/>/<see cref="GeneratedProductBinding"/>/
/// <see cref="GeneratedProductReapply"/> enums share identical wire values with the Core.Agent
/// <see cref="ScopeKind"/>/<see cref="BindingKind"/>/<see cref="ReapplyPolicy"/> enums (both
/// mirror the frozen product-control-v1 contract). We still map explicitly so a future
/// renumber on either side fails fast here rather than silently mis-binding an intent.
/// </summary>
public static class ProductCatalogProjection
{
    private const string TestFixturePrefix = "test.fixture.";

    /// <summary>Every generated product projected onto the Core.Agent submit shape.</summary>
    public static IReadOnlyList<ProductCatalogEntry> Entries { get; } =
        GeneratedProductDefinitionCatalog.Products.Select(Project).ToArray();

    /// <summary>
    /// Products that may be shown or submitted by user-facing surfaces. Draft definitions and
    /// generated fixtures stay in <see cref="Entries"/> for codegen/contract tests, but are not
    /// product features and must never appear in WPF/Web catalogs. Projection-driven preset
    /// products (reinforcement/secret-protocol preset execute) are also excluded: their intent
    /// is built by the Agent from its own projection state, so WPF/Web offer no generic submit
    /// entry for them. Other NativeWorkflow products (e.g. the selected-unit attribute
    /// modifiers) are submitted directly by the WPF Product Intent route and stay public.
    /// </summary>
    public static IReadOnlyList<ProductCatalogEntry> PublicEntries { get; } =
        Entries
            .Where(entry =>
                entry.Availability == GeneratedProductAvailability.Available &&
                !IsTestFixture(entry.ProductId.Value) &&
                !IsProjectionDrivenPreset(entry.ProductId.Value))
            .ToArray();

    /// <summary>Projects a single generated definition onto the Core.Agent submit shape.</summary>
    public static ProductCatalogEntry Project(GeneratedProductDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new ProductCatalogEntry(
            new ProductId(definition.ProductId),
            definition.DisplayName,
            definition.Availability,
            definition.Kind,
            MapScope(definition.Scope),
            MapBinding(definition.Binding),
            MapReapply(definition.Reapply),
            definition.Parameters
                .Select(parameter => new ProductCatalogParameter(parameter.Name, parameter.Kind))
                .ToArray());
    }

    /// <summary>Finds the projected entry for <paramref name="productId"/>, if the catalog ships it.</summary>
    public static bool TryGet(string productId, [MaybeNullWhen(false)] out ProductCatalogEntry entry)
    {
        return TryGetFrom(Entries, productId, out entry);
    }

    /// <summary>Finds an Available, non-fixture product that a user-facing surface may submit.</summary>
    public static bool TryGetPublic(
        string productId,
        [MaybeNullWhen(false)] out ProductCatalogEntry entry)
    {
        return TryGetFrom(PublicEntries, productId, out entry);
    }

    private static bool TryGetFrom(
        IReadOnlyList<ProductCatalogEntry> source,
        string productId,
        [MaybeNullWhen(false)] out ProductCatalogEntry entry)
    {
        foreach (var candidate in source)
        {
            if (string.Equals(candidate.ProductId.Value, productId, StringComparison.Ordinal))
            {
                entry = candidate;
                return true;
            }
        }

        entry = null;
        return false;
    }

    private static bool IsTestFixture(string productId) =>
        productId.StartsWith(TestFixturePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Preset-execute products whose intent is composed by the Agent from the preset
    /// projection state (command 62/63 publishers); they never take a generic WPF/Web submit.
    /// </summary>
    private static bool IsProjectionDrivenPreset(string productId) =>
        string.Equals(productId, "reinforcement.preset.execute", StringComparison.Ordinal) ||
        string.Equals(productId, "secretprotocol.preset.execute", StringComparison.Ordinal);

    private static ScopeKind MapScope(GeneratedProductScope scope) => scope switch
    {
        GeneratedProductScope.CurrentPlayer => ScopeKind.CurrentPlayer,
        GeneratedProductScope.AllOtherPlayers => ScopeKind.AllOtherPlayers,
        GeneratedProductScope.AllUnits => ScopeKind.AllUnits,
        GeneratedProductScope.SelectedUnit => ScopeKind.SelectedUnit,
        GeneratedProductScope.SelectedObject => ScopeKind.SelectedObject,
        GeneratedProductScope.FixedPlayer => ScopeKind.FixedPlayer,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown generated product scope."),
    };

    private static BindingKind MapBinding(GeneratedProductBinding binding) => binding switch
    {
        GeneratedProductBinding.Live => BindingKind.Live,
        GeneratedProductBinding.Rebindable => BindingKind.Rebindable,
        GeneratedProductBinding.Captured => BindingKind.Captured,
        _ => throw new ArgumentOutOfRangeException(nameof(binding), binding, "Unknown generated product binding."),
    };

    private static ReapplyPolicy MapReapply(GeneratedProductReapply reapply) => reapply switch
    {
        GeneratedProductReapply.None => ReapplyPolicy.None,
        GeneratedProductReapply.OnMapStart => ReapplyPolicy.OnMapStart,
        _ => throw new ArgumentOutOfRangeException(nameof(reapply), reapply, "Unknown generated product reapply policy."),
    };
}
