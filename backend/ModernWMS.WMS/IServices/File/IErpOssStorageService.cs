using Microsoft.AspNetCore.Http;
using ModernWMS.Core.DI;
using ModernWMS.WMS.Entities.ViewModels;

namespace ModernWMS.WMS.IServices;

/// <summary>
/// 使用 ERP 当前主 OSS 配置保存和读取 ModernWMS 图片。
/// </summary>
public interface IErpOssStorageService : IDependency
{
    Task<OssFileUploadViewModel> UploadImageAsync(IFormFile file, long shipmentId, string category);

    Task<string> CreateAccessUrlAsync(string path);
}
