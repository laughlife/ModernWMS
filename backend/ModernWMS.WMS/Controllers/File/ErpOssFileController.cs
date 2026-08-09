using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ModernWMS.Core.Controller;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Controllers;

/// <summary>
/// ModernWMS 使用 ERP 主 OSS 空间的图片读写接口。
/// </summary>
[Authorize]
[Route("file/erp-oss")]
[ApiController]
[ApiExplorerSettings(GroupName = "WMS")]
public class ErpOssFileController : BaseController
{
    private readonly IErpOssStorageService _erpOssStorageService;
    private readonly ILogger<ErpOssFileController> _logger;

    public ErpOssFileController(
        IErpOssStorageService erpOssStorageService,
        ILogger<ErpOssFileController> logger)
    {
        _erpOssStorageService = erpOssStorageService;
        _logger = logger;
    }

    /// <summary>
    /// 上传一张收货相关图片到 ERP 当前主 OSS 空间。
    /// </summary>
    [HttpPost("image")]
    [RequestSizeLimit(10 * 1024 * 1024 + 1024 * 128)]
    public async Task<ResultModel<OssFileUploadViewModel>> UploadImageAsync(
        [FromForm] IFormFile file,
        [FromForm] long shipmentId,
        [FromForm] string category)
    {
        try
        {
            var result = await _erpOssStorageService.UploadImageAsync(file, shipmentId, category);
            return ResultModel<OssFileUploadViewModel>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ResultModel<OssFileUploadViewModel>.Error(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "ERP OSS 配置不可用");
            return ResultModel<OssFileUploadViewModel>.Error(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "上传收货图片到 ERP OSS 失败");
            return ResultModel<OssFileUploadViewModel>.Error("OSS 图片上传失败，请稍后重试");
        }
    }

    /// <summary>
    /// 为 ModernWMS 自有 OSS 图片生成短期可读地址。
    /// </summary>
    [HttpPost("access-url")]
    public async Task<ResultModel<string>> CreateAccessUrlAsync(OssFileAccessRequest request)
    {
        try
        {
            var result = await _erpOssStorageService.CreateAccessUrlAsync(request.path);
            return ResultModel<string>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ResultModel<string>.Error(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "ERP OSS 配置不可用");
            return ResultModel<string>.Error(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成 ERP OSS 图片访问地址失败");
            return ResultModel<string>.Error("OSS 图片读取地址生成失败，请稍后重试");
        }
    }
}
