using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext.Entities;

namespace ModernWMS.Core.DBContext;

/// <summary>
/// Ruoyi 业务主数据库上下文。
/// 与 WMS 主数据库上下文独立注册、独立连接，并支持完整的 EF Core 读写操作。
/// Ruoyi 表按业务需要在此上下文中显式映射。
/// </summary>
public sealed class RuoyiDbContext : DbContext
{
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="options">上下文选项</param>
    public RuoyiDbContext(DbContextOptions<RuoyiDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// 系统部门
    /// </summary>
    public DbSet<ErpSystemDeptEntity> SystemDepts => Set<ErpSystemDeptEntity>();

    /// <summary>
    /// ModernWMS warehouse and ERP operator-group bindings.
    /// </summary>
    public DbSet<ErpWarehouseOperatorGroupEntity> WarehouseOperatorGroups => Set<ErpWarehouseOperatorGroupEntity>();

    /// <summary>
    /// 系统用户
    /// </summary>
    public DbSet<ErpSystemUserEntity> SystemUsers => Set<ErpSystemUserEntity>();

    /// <summary>
    /// 供应商
    /// </summary>
    public DbSet<ErpSupplierEntity> Suppliers => Set<ErpSupplierEntity>();

    /// <summary>
    /// ERP 仓库
    /// </summary>
    public DbSet<ErpWarehouseEntity> Warehouses => Set<ErpWarehouseEntity>();

    /// <summary>
    /// ERP logistics shipment facts.
    /// </summary>
    public DbSet<ErpLogisticsInfoEntity> LogisticsInfos => Set<ErpLogisticsInfoEntity>();

    /// <summary>
    /// ERP logistics tracking snapshots.
    /// </summary>
    public DbSet<ErpTrackEntity> Tracks => Set<ErpTrackEntity>();

    /// <summary>
    /// ERP logistics tracking events.
    /// </summary>
    public DbSet<ErpTrackEventEntity> TrackEvents => Set<ErpTrackEventEntity>();

    /// <summary>
    /// ERP 文件存储配置（只读）。
    /// </summary>
    public DbSet<ErpFileConfigEntity> FileConfigs => Set<ErpFileConfigEntity>();

    /// <summary>
    /// 显式映射当前已接入 ModernWMS 的 Ruoyi 业务实体。
    /// </summary>
    /// <param name="modelBuilder">model builder</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ErpSystemDeptEntity>(entity =>
        {
            entity.ToTable("system_dept");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpWarehouseOperatorGroupEntity>(entity =>
        {
            entity.ToTable("wms_warehouse_operator_group");
            entity.HasKey(t => t.id);
            entity.HasIndex(t => new { t.tenant_id, t.warehouse_id, t.dept_id }).IsUnique();
            entity.Property(t => t.id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<ErpSystemUserEntity>(entity =>
        {
            entity.ToTable("system_users");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpSupplierEntity>(entity =>
        {
            entity.ToTable("erp_supplier");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpWarehouseEntity>(entity =>
        {
            entity.ToTable("erp_warehouse");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpLogisticsInfoEntity>(entity =>
        {
            entity.ToTable("trk_logistics_info");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpTrackEntity>(entity =>
        {
            entity.ToTable("trk_track");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpTrackEventEntity>(entity =>
        {
            entity.ToTable("trk_track_event");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpFileConfigEntity>(entity =>
        {
            entity.ToTable("infra_file_config");
            entity.HasKey(t => t.id);
        });

        base.OnModelCreating(modelBuilder);
    }
}
