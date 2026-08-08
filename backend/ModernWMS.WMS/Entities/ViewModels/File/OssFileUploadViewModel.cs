namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// OSS 图片上传结果。
/// </summary>
public class OssFileUploadViewModel
{
    public string name { get; set; } = string.Empty;

    public string path { get; set; } = string.Empty;

    public string url { get; set; } = string.Empty;

    public string access_url { get; set; } = string.Empty;

    public string content_type { get; set; } = string.Empty;

    public long size { get; set; }
}

/// <summary>
/// OSS 对象读取地址请求。
/// </summary>
public class OssFileAccessRequest
{
    public string path { get; set; } = string.Empty;
}
