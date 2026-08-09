namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// ERP operator group available for warehouse binding.
/// </summary>
public class OperatorGroupOptionViewModel
{
    public long id { get; set; }

    public string name { get; set; } = string.Empty;

    public int sort { get; set; }
}
