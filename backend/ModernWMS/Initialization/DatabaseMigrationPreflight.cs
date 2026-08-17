namespace ModernWMS.Initialization;

/// <summary>
/// Read-only view of the migration history and physical tables in the current database.
/// </summary>
public sealed class DatabaseSchemaSnapshot
{
    public DatabaseSchemaSnapshot(
        IEnumerable<string> appliedMigrations,
        IEnumerable<string> tables)
    {
        AppliedMigrations = appliedMigrations.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Tables = tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> AppliedMigrations { get; }

    public IReadOnlySet<string> Tables { get; }
}

public interface IDatabaseSchemaInspector
{
    Task<DatabaseSchemaSnapshot> InspectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Prevents EF from running a known non-transactional rename migration over a schema that
/// already contains some or all of that migration's physical output.
/// </summary>
public sealed class DatabaseMigrationPreflight(IDatabaseSchemaInspector inspector)
{
    public const string WmsPrefixMigrationId = "20260809062805_UnifyDatabaseWithWmsPrefix";

    private static readonly string[] RenamedTables =
    [
        "warehousearea",
        "warehouse",
        "userrole",
        "user_defined_print_solution",
        "user",
        "supplier",
        "stocktaking",
        "stockprocessdetail",
        "stockprocess",
        "stockmove",
        "stockfreeze",
        "stockadjust",
        "stock",
        "spu",
        "sku_safety_stock",
        "sku",
        "rolemenu",
        "menu",
        "goodsowner",
        "goodslocation",
        "global_unique_serial",
        "freightfee",
        "flowsetusers",
        "flowsetmain",
        "flowsetfilter",
        "flowset",
        "dispatchpicklist",
        "dispatchlist",
        "company",
        "asnsort",
        "asnmaster",
        "asn",
        "action_log"
    ];

    public async Task EnsureSafeAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await inspector.InspectAsync(cancellationToken);
        if (snapshot.AppliedMigrations.Contains(WmsPrefixMigrationId))
        {
            var missingTargets = RenamedTables
                .Select(table => $"wms_{table}")
                .Where(table => !snapshot.Tables.Contains(table))
                .OrderBy(table => table, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missingTargets.Length > 0)
            {
                throw new InvalidOperationException(
                    $"检测到数据库迁移历史与物理结构不一致：迁移历史已记录 {WmsPrefixMigrationId}，" +
                    $"但迁移目标表不完整，缺少：{Describe(missingTargets)}。" +
                    "禁止自动继续迁移。请先备份数据库并由人工核对、修复物理结构；" +
                    "启动预检不会自动创建表、DROP 表或修改迁移历史。");
            }

            var recreatedOldTables = RenamedTables
                .Where(snapshot.Tables.Contains)
                .OrderBy(table => table, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (recreatedOldTables.Length > 0)
            {
                throw new InvalidOperationException(
                    $"检测到数据库迁移历史与物理结构不一致：迁移历史已记录 {WmsPrefixMigrationId}，" +
                    $"但数据库中又出现了已迁移的旧裸表：{Describe(recreatedOldTables)}。" +
                    "这通常表示旧版本程序或错误的迁移历史配置再次污染了共享数据库。" +
                    "禁止自动继续迁移。请先停止旧版本程序、备份数据库，并由人工核对旧表与 wms_ 表数据；" +
                    "启动预检不会自动 DROP 表或修改迁移历史。");
            }

            return;
        }

        var prefixedTables = RenamedTables
            .Select(table => $"wms_{table}")
            .Where(snapshot.Tables.Contains)
            .OrderBy(table => table, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (prefixedTables.Length == 0)
        {
            if (snapshot.AppliedMigrations.Count == 0)
            {
                var unexpectedOldTables = RenamedTables
                    .Where(snapshot.Tables.Contains)
                    .OrderBy(table => table, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (unexpectedOldTables.Length > 0)
                {
                    throw new InvalidOperationException(
                        "检测到数据库迁移历史与物理结构不一致：迁移历史为空，" +
                        $"但数据库中已经存在旧 WMS 表：{Describe(unexpectedOldTables)}。" +
                        "该数据库不是真正空库，执行初始迁移可能因对象已存在而失败。" +
                        "禁止自动继续迁移。请先备份数据库并由人工核对物理结构及迁移历史；" +
                        "启动预检不会自动创建表、DROP 表或修改迁移历史。");
                }
            }

            if (snapshot.AppliedMigrations.Count > 0)
            {
                var missingOldTables = RenamedTables
                    .Where(table => !snapshot.Tables.Contains(table))
                    .OrderBy(table => table, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (missingOldTables.Length > 0)
                {
                    throw new InvalidOperationException(
                        $"检测到数据库迁移历史与物理结构不一致：迁移 {WmsPrefixMigrationId} 尚未执行，" +
                        $"但其旧源表不完整，缺少：{Describe(missingOldTables)}。" +
                        "继续迁移可能在删除外键、主键或改名过程中失败，并留下更多 MySQL 非事务 DDL 半成品。" +
                        "禁止自动继续迁移。请先备份数据库并由人工核对、修复旧 schema；" +
                        "启动预检不会自动创建表、DROP 表或修改迁移历史。");
                }
            }

            return;
        }

        var oldTables = RenamedTables
            .Where(snapshot.Tables.Contains)
            .OrderBy(table => table, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        throw new InvalidOperationException(
            $"检测到数据库迁移历史与物理结构不一致：迁移 {WmsPrefixMigrationId} 未记录在 " +
            "wms_ef_migrations_history 中，但数据库已经存在该迁移目标表。" +
            $"旧表样例：{Describe(oldTables)}；wms_ 表样例：{Describe(prefixedTables)}。" +
            "这通常表示 MySQL 非事务 DDL 曾部分执行；再次直接运行会继续破坏或覆盖现有结构。" +
            "禁止自动继续迁移。请先备份数据库，逐表核对旧表与 wms_ 表的数据、主键、索引和外键，" +
            "再由人工显式修复物理结构及迁移历史。启动预检不会自动 DROP 表，也不会补写迁移历史。");
    }

    private static string Describe(IReadOnlyList<string> tables)
    {
        const int maximumExamples = 5;
        return tables.Count == 0
            ? "无"
            : string.Join(", ", tables.Take(maximumExamples))
                + (tables.Count > maximumExamples ? $"（另有 {tables.Count - maximumExamples} 个）" : string.Empty);
    }
}
