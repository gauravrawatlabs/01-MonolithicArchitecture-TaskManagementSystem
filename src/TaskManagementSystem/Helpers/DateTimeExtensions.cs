namespace TaskManagementSystem.Helpers;

public static class DateTimeExtensions
{
    /// <summary>
    /// Normalizes an incoming DateTime to UTC.
    /// JSON "2026-12-01T17:00:00Z" arrives as Kind=Utc; "2026-12-01T17:00:00" (no offset) arrives as
    /// Kind=Unspecified. Our API contract says these fields are UTC ("...Utc" names), so Unspecified is
    /// treated as UTC — NOT converted from the server's local time zone, which would silently shift it.
    /// "2026-12-01T17:00:00+05:30" arrives as Kind=Local (converted to server time) → converted to UTC.
    /// </summary>
    public static DateTime AsUtc(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime()
    };
}
