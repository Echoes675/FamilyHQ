using System.Runtime.CompilerServices;
using FamilyHQ.Data.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

[assembly: InternalsVisibleTo("FamilyHQ.Services.Tests")]

namespace FamilyHQ.Data.PostgreSQL.Infrastructure;

public sealed class UniqueConstraintExceptionInterceptor : SaveChangesInterceptor
{
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Exception is DbUpdateException dbEx && IsUniqueConstraintViolation(dbEx))
            throw new UniqueConstraintException(dbEx.Message, dbEx);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken ct = default)
    {
        if (eventData.Exception is DbUpdateException dbEx && IsUniqueConstraintViolation(dbEx))
            throw new UniqueConstraintException(dbEx.Message, dbEx);

        return Task.CompletedTask;
    }

    internal static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };
}
