namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// operator group detail
/// </summary>
public class OperatorGroupViewModel
{
    /// <summary>
    /// sequence number
    /// </summary>
    public int sequence { get; set; }

    /// <summary>
    /// group name
    /// </summary>
    public string group_name { get; set; } = string.Empty;

    /// <summary>
    /// leader name
    /// </summary>
    public string leader_name { get; set; } = string.Empty;

    /// <summary>
    /// leader phone
    /// </summary>
    public string phone { get; set; } = string.Empty;
}
