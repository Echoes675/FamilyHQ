using FamilyHQ.Data.Exceptions;
using FamilyHQ.Data.PostgreSQL.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FamilyHQ.Services.Tests.Infrastructure;

public class UniqueConstraintExceptionInterceptorTests
{
    [Fact]
    public void IsUniqueConstraintViolation_ReturnsFalse_WhenInnerIsNonPostgresException()
    {
        var ex = new DbUpdateException("transient error", new Exception("connection refused"));
        UniqueConstraintExceptionInterceptor.IsUniqueConstraintViolation(ex).Should().BeFalse();
    }

    [Fact]
    public void IsUniqueConstraintViolation_ReturnsFalse_WhenInnerExceptionIsNull()
    {
        var ex = new DbUpdateException("error");
        UniqueConstraintExceptionInterceptor.IsUniqueConstraintViolation(ex).Should().BeFalse();
    }
}
