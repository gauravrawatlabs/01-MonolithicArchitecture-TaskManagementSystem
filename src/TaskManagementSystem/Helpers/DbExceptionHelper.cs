using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace TaskManagementSystem.Helpers;

/// <summary>Recognises specific SQL Server errors wrapped inside EF Core's DbUpdateException.</summary>
public static class DbExceptionHelper
{
    // 2601 = duplicate key in a UNIQUE INDEX, 2627 = violation of a UNIQUE/PRIMARY KEY constraint.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    /// <summary>
    /// True when SaveChanges failed because a unique index/constraint was violated — e.g. two
    /// requests creating the same category name at the same moment, both passing the
    /// "name exists?" check before either one saved (a race condition).
    /// </summary>
    public static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation };
}
