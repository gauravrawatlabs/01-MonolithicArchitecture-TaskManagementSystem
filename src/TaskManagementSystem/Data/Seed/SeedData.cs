namespace TaskManagementSystem.Data.Seed;

/// <summary>
/// Fixed values used by HasData() seeding. They MUST be constants: if a seed value changed
/// on every build (Guid.NewGuid(), DateTime.UtcNow), EF Core would detect a model change
/// every time and generate a new migration.
/// </summary>
public static class SeedData
{
    public static readonly DateTime SeedTimestampUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static readonly Guid AdminRoleId = Guid.Parse("92f31baa-aead-45a1-a092-684fa4b31bdb");
    public static readonly Guid ManagerRoleId = Guid.Parse("096d8c43-cef0-4ab8-9a88-4ea081d03519");
    public static readonly Guid UserRoleId = Guid.Parse("925c9990-ebff-4769-9ef2-0bef17165d35");

    // Identity uses ConcurrencyStamp for optimistic concurrency; seeded rows need a fixed value.
    public const string AdminRoleConcurrencyStamp = "13fccea7-aa12-4d6e-b602-afd55fae3d57";
    public const string ManagerRoleConcurrencyStamp = "0fb1258b-7ae7-4b85-9b64-b286daf61dd8";
    public const string UserRoleConcurrencyStamp = "7cb8867b-cd7f-4357-9ad0-d2092ddf9acf";
}
