namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// 库区「组员筛选」下拉选项：操作小组（system_dept.dept='operator'）及其子部门下的成员。
/// 展示为「小组名称 / 组员名称」，筛选时按成员所属小组匹配库区。
/// </summary>
public class OperatorGroupMemberOptionViewModel
{
    /// <summary>
    /// 成员用户 ID（system_users.id）
    /// </summary>
    public long user_id { get; set; }

    /// <summary>
    /// 组员名称（system_users.nickname）
    /// </summary>
    public string member_name { get; set; } = string.Empty;

    /// <summary>
    /// 所属操作小组部门 ID（system_dept.id）
    /// </summary>
    public long group_id { get; set; }

    /// <summary>
    /// 小组名称（system_dept.name）
    /// </summary>
    public string group_name { get; set; } = string.Empty;
}
