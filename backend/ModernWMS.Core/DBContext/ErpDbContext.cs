using Microsoft.EntityFrameworkCore;

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
}