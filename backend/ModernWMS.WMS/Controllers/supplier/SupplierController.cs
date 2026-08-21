using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Controller;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Controllers
{
    /// <summary>
    /// supplier controller
    /// </summary>
    [Route("supplier")]
    [ApiController]
    [ApiExplorerSettings(GroupName = "Base")]
    public class SupplierController : BaseController
    {
        private readonly ISupplierService _supplierService;
        private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;

        /// <summary>
        /// 初始化 SupplierController 的新实例。
        /// </summary>
        public SupplierController(
            ISupplierService supplierService,
            IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer)
        {
            _supplierService = supplierService;
            _stringLocalizer = stringLocalizer;
        }

        /// <summary>
        /// page search
        /// </summary>
        [HttpPost("list")]
        public async Task<ResultModel<PageData<SupplierViewModel>>> PageAsync(PageSearch pageSearch)
        {
            var (data, totals) = await _supplierService.PageAsync(pageSearch, CurrentUser);

            return ResultModel<PageData<SupplierViewModel>>.Success(new PageData<SupplierViewModel>
            {
                Rows = data,
                Totals = totals
            });
        }

        /// <summary>
        /// get all records
        /// </summary>
        [HttpGet("all")]
        public async Task<ResultModel<List<SupplierViewModel>>> GetAllAsync()
        {
            var data = await _supplierService.GetAllAsync();
            return ResultModel<List<SupplierViewModel>>.Success(data);
        }

        /// <summary>
        /// get a record by id
        /// </summary>
        [HttpGet]
        public async Task<ResultModel<SupplierViewModel>> GetAsync(long id)
        {
            var data = await _supplierService.GetAsync(id);
            if (data != null)
            {
                return ResultModel<SupplierViewModel>.Success(data);
            }

            return ResultModel<SupplierViewModel>.Error(_stringLocalizer["not_exists_entity"]);
        }
    }
}
