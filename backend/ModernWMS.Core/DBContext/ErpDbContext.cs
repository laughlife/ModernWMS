using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext.Entities;

namespace ModernWMS.Core.DBContext;

/// <summary>
/// ERP 数据库上下文（双数据源中的第二个数据源）。
/// 注意：不要继承 SqlDBContext，避免把 WMS 实体自动映射到 ERP 库。
/// ERP 相关实体后续按需在此上下文中显式添加。
/// </summary>
public sealed class ErpDbContext : DbContext
{
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="options">上下文选项</param>
    public ErpDbContext(DbContextOptions<ErpDbContext> options) : base(options)
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
    /// 显式映射 ERP 只读实体，避免自动映射 WMS 主库实体。
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

        base.OnModelCreating(modelBuilder);
    }
}
