using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace FamilyHQ.Data.Infrastructure;

/// <summary>
/// Runs at model finalization — after the provider's <c>IModelCustomizer</c> has had its chance to add
/// tokens — and enforces <see cref="ConcurrencyTokenModelGuard"/> for whatever provider is active.
/// Wired into the shared <c>FamilyHqDbContext</c> so no provider can opt out (FHQ-146 / FHQ-119).
/// </summary>
public sealed class RequireConcurrencyTokenConvention : IModelFinalizedConvention
{
    public IModel ProcessModelFinalized(IModel model)
    {
        ConcurrencyTokenModelGuard.EnsureConcurrencyTokens(model);
        return model;
    }
}
