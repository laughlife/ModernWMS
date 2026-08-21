using Microsoft.AspNetCore.Http;
using ModernWMS.Core.DI;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// 使用 ERP 当前主 OSS 配置保存和读取 ModernWMS 图片。
/// </summary>
public interface IErpOssStorageService : IDependency
{
    /// <summary>
    /// 定义 UploadImageAsync 操作。
    /// </summary>
    Task<OssFileUploadViewModel> UploadImageAsync(IFormFile file, long shipmentId, string category);

    /// <summary>
    /// 定义 CreateAccessUrlAsync 操作。
    /// </summary>
    Task<string> CreateAccessUrlAsync(string path);
}
