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
    /// 系统用户
    /// </summary>
    public DbSet<ErpSystemUserEntity> SystemUsers => Set<ErpSystemUserEntity>();

    /// <summary>
    /// 供应商
    /// </summary>
    public DbSet<ErpSupplierEntity> Suppliers => Set<ErpSupplierEntity>();

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

        base.OnModelCreating(modelBuilder);
    }
}
