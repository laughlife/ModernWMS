using Dapper;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services
{
    /// <summary>
    /// Supplier Service
    /// </summary>
    public class SupplierService : BaseService<SupplierEntity>, ISupplierService
    {
        private const string Projection = """
            `id`,
            COALESCE(`name`, '') AS `supplier_name`,
            COALESCE(`name`, '') AS `name`,
            COALESCE(`linkman`, '') AS `linkman`,
            COALESCE(`telephone_num`, '') AS `telephone_num`,
            COALESCE(`qq`, '') AS `qq`,
            COALESCE(`email`, '') AS `email`,
            COALESCE(`province_name`, '') AS `province_name`,
            COALESCE(`city_name`, '') AS `city_name`,
            COALESCE(`address_line`, '') AS `address_line`,
            COALESCE(`remark`, '') AS `remark`
            """;

        private readonly IMySqlConnectionFactory _connectionFactory;

        /// <summary>
        /// 初始化供应商查询服务。
        /// </summary>
        /// <param name="connectionFactory">MySQL 连接工厂。</param>
        public SupplierService(IMySqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        /// <summary>
        /// page search
        /// </summary>
        public async Task<(List<SupplierViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
        {
            var supplierNameKeyword = pageSearch.searchObjects
                .FirstOrDefault(t => string.Equals(t.Name, "name", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t.Name, "supplier_name", StringComparison.OrdinalIgnoreCase))
                ?.Text
                ?.Trim();

            var keywordClause = string.IsNullOrWhiteSpace(supplierNameKeyword)
                ? string.Empty
                : " AND `name` LIKE @keyword ESCAPE '!'";
            var parameters = new
            {
                keyword = string.IsNullOrWhiteSpace(supplierNameKeyword)
                    ? null
                    : $"%{EscapeLike(supplierNameKeyword)}%",
                offset = (pageSearch.pageIndex - 1) * pageSearch.pageSize,
                pageSize = pageSearch.pageSize
            };

            await using var connection = await _connectionFactory.OpenConnectionAsync();
            using var result = await connection.QueryMultipleAsync($"""
                SELECT COUNT(*)
                FROM `erp_supplier`
                WHERE `deleted` = 0{keywordClause};

                SELECT {Projection}
                FROM `erp_supplier`
                WHERE `deleted` = 0{keywordClause}
                ORDER BY `id` DESC
                LIMIT @pageSize OFFSET @offset;
                """, parameters);
            var totals = await result.ReadSingleAsync<int>();
            var list = (await result.ReadAsync<SupplierViewModel>()).AsList();

            return (list, totals);
        }

        /// <summary>
        /// Get all records
        /// </summary>
        public async Task<List<SupplierViewModel>> GetAllAsync()
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            var rows = await connection.QueryAsync<SupplierViewModel>($"""
                SELECT {Projection}
                FROM `erp_supplier`
                WHERE `deleted` = 0
                ORDER BY `name`;
                """);
            return rows.AsList();
        }

        /// <summary>
        /// Get a record by id
        /// </summary>
        public async Task<SupplierViewModel?> GetAsync(long id)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            return await connection.QuerySingleOrDefaultAsync<SupplierViewModel>($"""
                SELECT {Projection}
                FROM `erp_supplier`
                WHERE `deleted` = 0 AND `id` = @id
                LIMIT 1;
                """, new { id });
        }

        private static string EscapeLike(string value) => value
            .Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal);
    }
}
