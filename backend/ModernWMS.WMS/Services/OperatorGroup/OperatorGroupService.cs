using Dapper;
using ModernWMS.Core.Database;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

/// <summary>
/// OperatorGroupService
/// </summary>
public class OperatorGroupService : IOperatorGroupService
{
    /// <summary>
    /// Shared ERP/WMS MySQL connection factory.
    /// </summary>
    private readonly IMySqlConnectionFactory _connectionFactory;

    /// <summary>
    /// constructor
    /// </summary>
    /// <param name="connectionFactory">shared MySQL connection factory</param>
    public OperatorGroupService(IMySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Get all operator group details from ERP.
    /// </summary>
    /// <returns>operator group list</returns>
    public async Task<List<OperatorGroupViewModel>> GetAllAsync()
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync();
        var rows = (await connection.QueryAsync<OperatorGroupViewModel>("""
            SELECT
                COALESCE(dept.`name`, '') AS `group_name`,
                COALESCE(leader.`nickname`, '') AS `leader_name`,
                COALESCE(leader.`mobile`, '') AS `phone`
            FROM `system_dept` AS dept
            LEFT JOIN `system_users` AS leader
                ON leader.`id` = dept.`leader_user_id`
                AND leader.`deleted` = 0
            WHERE dept.`dept` = 'operator'
                AND dept.`deleted` = 0
            ORDER BY dept.`sort`, dept.`id`;
            """)).AsList();

        for (var index = 0; index < rows.Count; index++)
        {
            rows[index].sequence = index + 1;
        }

        return rows;
    }
}
