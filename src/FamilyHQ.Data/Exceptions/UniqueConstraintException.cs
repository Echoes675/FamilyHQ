using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Exceptions;

public sealed class UniqueConstraintException(string message, DbUpdateException inner)
    : DbUpdateException(message, inner);
