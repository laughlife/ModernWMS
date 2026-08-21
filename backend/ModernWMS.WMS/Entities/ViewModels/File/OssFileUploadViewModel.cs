namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// OSS 图片上传结果。
/// </summary>
public class OssFileUploadViewModel
{
    /// <summary>
    /// 获取或设置 name。
    /// </summary>
    public string name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 path。
    /// </summary>
    public string path { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 url。
    /// </summary>
    public string url { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 access_url。
    /// </summary>
    public string access_url { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 content_type。
    /// </summary>
    public string content_type { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 size。
    /// </summary>
    public long size { get; set; }
}

/// <summary>
/// OSS 对象读取地址请求。
/// </summary>
public class OssFileAccessRequest
{
    /// <summary>
    /// 获取或设置 path。
    /// </summary>
    public string path { get; set; } = string.Empty;
}
