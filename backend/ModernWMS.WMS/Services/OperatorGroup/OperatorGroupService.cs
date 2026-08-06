using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

/// <summary>
/// OperatorGroupService
/// </summary>
public class OperatorGroupService : IOperatorGroupService
{
    /// <summary>
    /// Ruoyi primary DBContext
    /// </summary>
    private readonly RuoyiDbContext _ruoyiDbContext;

    /// <summary>
    /// constructor
    /// </summary>
    /// <param name="ruoyiDbContext">Ruoyi primary DBContext</param>
    public OperatorGroupService(RuoyiDbContext ruoyiDbContext)
    {
        this._ruoyiDbContext = ruoyiDbContext;
    }

    /// <summary>
    /// Get all operator group details from ERP.
    /// </summary>
    /// <returns>operator group list</returns>
    public async Task<List<OperatorGroupViewModel>> GetAllAsync()
    {
        var query =
            from dept in _ruoyiDbContext.SystemDepts.AsNoTracking()
            join user in _ruoyiDbContext.SystemUsers.AsNoTracking().Where(t => !t.deleted)
                on dept.leader_user_id equals (long?)user.id into userGroup
            from leader in userGroup.DefaultIfEmpty()
            where dept.dept == "operator" && !dept.deleted
            orderby dept.sort, dept.id
            select new
            {
                group_name = dept.name ?? string.Empty,
                leader_name = leader == null ? string.Empty : leader.nickname ?? string.Empty,
                phone = leader == null ? string.Empty : leader.mobile ?? string.Empty
            };

        var data = await query.ToListAsync();
        return data.Select((item, index) => new OperatorGroupViewModel
        {
            sequence = index + 1,
            group_name = item.group_name,
            leader_name = item.leader_name,
            phone = item.phone
        }).ToList();
    }
}
