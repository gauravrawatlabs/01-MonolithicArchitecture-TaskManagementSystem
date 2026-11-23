using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TaskManagementSystem.Data.Converters;

/// <summary>
/// SQL Server's datetime2 stores no time-zone information, so EF Core reads values back with
/// DateTimeKind.Unspecified and JSON serializes them WITHOUT the trailing "Z"
/// ("2026-01-01T00:00:00" instead of "2026-01-01T00:00:00Z"). Clients then assume local time.
///
/// This app stores all DateTime values in UTC, so on READ we mark them as UTC.
/// On WRITE we convert any non-UTC value to UTC as a safety net.
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    toDatabase => toDatabase.Kind == DateTimeKind.Utc ? toDatabase : toDatabase.ToUniversalTime(),
    fromDatabase => DateTime.SpecifyKind(fromDatabase, DateTimeKind.Utc));
