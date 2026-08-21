namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// ERP operator group available for warehouse binding.
/// </summary>
public class OperatorGroupOptionViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }

    /// <summary>
    /// 获取或设置 name。
    /// </summary>
    public string name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 sort。
    /// </summary>
    public int sort { get; set; }
}
