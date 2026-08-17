using System.Data;
using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.DynamicSearch;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

internal enum PrintSolutionWriteStatus
{
    Succeeded,
    NotFound,
    Failed
}

internal sealed record PrintSolutionData(
    int id,
    string vue_path,
    string tab_page,
    string solution_name,
    string config_json,
    decimal report_length,
    decimal report_width,
    string report_direction,
    DateTime last_update_time,
    long tenant_id);

internal interface IPrintSolutionDataSource
{
    Task<(List<PrintSolutionData> Rows, int Total)> PageAsync(PageSearch pageSearch, long tenantId);
    Task<List<PrintSolutionData>> GetAllAsync(long tenantId);
    Task<PrintSolutionData?> GetAsync(int id);
    Task<List<PrintSolutionData>> GetByPathAsync(string vuePath, string tabPage, long tenantId);
    Task<int> AddAsync(PrintSolutionData row);
    Task<PrintSolutionWriteStatus> UpdateAsync(PrintSolutionData row);
    Task<bool> DeleteAsync(int id);
}

/// <summary>User-defined print solution service.</summary>
public class PrintSolutionService : BaseService<PrintSolutionEntity>, IPrintSolutionService
{
    private readonly IPrintSolutionDataSource _dataSource;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;

    /// <summary>Initializes the service with the shared MySQL connection factory.</summary>
    public PrintSolutionService(
        IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer)
        : this(new DapperPrintSolutionDataSource(connectionFactory), stringLocalizer)
    {
    }

    internal PrintSolutionService(
        IPrintSolutionDataSource dataSource,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
    }

    /// <summary>Page search for the current tenant.</summary>
    public async Task<(List<PrintSolutionViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch,
        CurrentUser currentUser)
    {
        var (rows, total) = await _dataSource.PageAsync(pageSearch, currentUser.tenant_id);
        return (rows.Select(ToViewModel).ToList(), total);
    }

    /// <summary>Gets all records for the current tenant.</summary>
    public async Task<List<PrintSolutionViewModel>> GetAllAsync(CurrentUser currentUser) =>
        (await _dataSource.GetAllAsync(currentUser.tenant_id)).Select(ToViewModel).ToList();

    /// <summary>Gets a record by id.</summary>
    public async Task<PrintSolutionViewModel> GetAsync(int id)
    {
        var row = await _dataSource.GetAsync(id);
        return row == null ? null! : ToViewModel(row);
    }

    /// <summary>Gets records for a Vue path and tab in the current tenant.</summary>
    public async Task<List<PrintSolutionViewModel>> GetByPathAsync(
        PrintSolutionGetByPathInputViewModel input,
        CurrentUser currentUser) =>
        (await _dataSource.GetByPathAsync(input.vue_path, input.tab_page, currentUser.tenant_id))
        .Select(ToViewModel)
        .ToList();

    /// <summary>Adds a print solution for the current tenant.</summary>
    public async Task<(int id, string msg)> AddAsync(
        PrintSolutionViewModel viewModel,
        CurrentUser currentUser)
    {
        var id = await _dataSource.AddAsync(new PrintSolutionData(
            0,
            viewModel.vue_path,
            viewModel.tab_page,
            viewModel.solution_name,
            viewModel.config_json,
            viewModel.report_length,
            viewModel.report_width,
            viewModel.report_direction,
            DateTime.Now,
            currentUser.tenant_id));

        return id > 0
            ? (id, _stringLocalizer["save_success"])
            : (0, _stringLocalizer["save_failed"]);
    }

    /// <summary>Updates the editable fields of a print solution.</summary>
    public async Task<(bool flag, string msg)> UpdateAsync(PrintSolutionViewModel viewModel)
    {
        var result = await _dataSource.UpdateAsync(new PrintSolutionData(
            viewModel.id,
            viewModel.vue_path,
            viewModel.tab_page,
            viewModel.solution_name,
            viewModel.config_json,
            viewModel.report_length,
            viewModel.report_width,
            viewModel.report_direction,
            DateTime.Now,
            viewModel.tenant_id));

        return result switch
        {
            PrintSolutionWriteStatus.NotFound => (false, _stringLocalizer["not_exists_entity"]),
            PrintSolutionWriteStatus.Succeeded => (true, _stringLocalizer["save_success"]),
            _ => (false, _stringLocalizer["save_failed"])
        };
    }

    /// <summary>Deletes a print solution by id.</summary>
    public async Task<(bool flag, string msg)> DeleteAsync(int id)
    {
        var deleted = await _dataSource.DeleteAsync(id);
        return deleted
            ? (true, _stringLocalizer["delete_success"])
            : (false, _stringLocalizer["delete_failed"]);
    }

    private static PrintSolutionViewModel ToViewModel(PrintSolutionData row) => new()
    {
        id = row.id,
        vue_path = row.vue_path,
        tab_page = row.tab_page,
        solution_name = row.solution_name,
        config_json = row.config_json,
        report_length = row.report_length,
        report_width = row.report_width,
        report_direction = row.report_direction,
        tenant_id = row.tenant_id
    };

    private sealed class DapperPrintSolutionDataSource : IPrintSolutionDataSource
    {
        private const string Projection = """
            solution.`id`,
            solution.`vue_path`,
            solution.`tab_page`,
            solution.`solution_name`,
            solution.`config_json`,
            solution.`report_length`,
            solution.`report_width`,
            solution.`report_direction`,
            solution.`last_update_time`,
            solution.`tenant_id`
            """;

        private static readonly IReadOnlyDictionary<string, string> SearchColumns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "solution.`id`",
                ["vue_path"] = "solution.`vue_path`",
                ["tab_page"] = "solution.`tab_page`",
                ["solution_name"] = "solution.`solution_name`",
                ["config_json"] = "solution.`config_json`",
                ["report_length"] = "solution.`report_length`",
                ["report_width"] = "solution.`report_width`",
                ["report_direction"] = "solution.`report_direction`",
                ["last_update_time"] = "solution.`last_update_time`",
                ["tenant_id"] = "solution.`tenant_id`"
            };

        private readonly IMySqlConnectionFactory _connectionFactory;

        public DapperPrintSolutionDataSource(IMySqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory
                ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<(List<PrintSolutionData> Rows, int Total)> PageAsync(
            PageSearch pageSearch,
            long tenantId)
        {
            var supportedFilters = pageSearch.searchObjects
                .Where(filter =>
                    SearchColumns.ContainsKey(filter.Name)
                    && !string.IsNullOrWhiteSpace(filter.Text)
                    && filter.Operator is >= Operators.Equal and <= Operators.Contains)
                .Select(NormalizeFilter);
            var filter = DapperSearchBuilder.Build(supportedFilters, SearchColumns);
            var where = string.IsNullOrWhiteSpace(filter.Sql)
                ? "solution.`tenant_id` = @tenant_id"
                : $"solution.`tenant_id` = @tenant_id AND {filter.Sql}";
            filter.Parameters.Add("tenant_id", tenantId);
            filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
            filter.Parameters.Add("page_size", pageSearch.pageSize);

            await using var connection = await _connectionFactory.OpenConnectionAsync();
            using var result = await connection.QueryMultipleAsync($"""
                SELECT COUNT(*)
                FROM `wms_user_defined_print_solution` AS solution
                WHERE {where};

                SELECT {Projection}
                FROM `wms_user_defined_print_solution` AS solution
                WHERE {where}
                ORDER BY solution.`id` DESC
                LIMIT @page_size OFFSET @offset;
                """, filter.Parameters);
            var total = await result.ReadSingleAsync<int>();
            var rows = (await result.ReadAsync<PrintSolutionData>()).AsList();
            return (rows, total);
        }

        private static SearchObject NormalizeFilter(SearchObject filter)
        {
            var text = filter.Text;
            if (string.Equals(filter.Type, "DATETIMEPICKER", StringComparison.OrdinalIgnoreCase)
                && filter.Operator is Operators.LessThan or Operators.LessThanOrEqual)
            {
                text = Convert.ToDateTime(text).ToString("yyyy-MM-dd") + " 23:59:59";
            }

            return new SearchObject
            {
                Name = filter.Name,
                Operator = filter.Operator,
                Text = text
            };
        }

        public async Task<List<PrintSolutionData>> GetAllAsync(long tenantId)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            return (await connection.QueryAsync<PrintSolutionData>($"""
                SELECT {Projection}
                FROM `wms_user_defined_print_solution` AS solution
                WHERE solution.`tenant_id` = @tenantId;
                """, new { tenantId })).AsList();
        }

        public async Task<PrintSolutionData?> GetAsync(int id)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            return await connection.QuerySingleOrDefaultAsync<PrintSolutionData>($"""
                SELECT {Projection}
                FROM `wms_user_defined_print_solution` AS solution
                WHERE solution.`id` = @id
                LIMIT 1;
                """, new { id });
        }

        public async Task<List<PrintSolutionData>> GetByPathAsync(
            string vuePath,
            string tabPage,
            long tenantId)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            return (await connection.QueryAsync<PrintSolutionData>($"""
                SELECT {Projection}
                FROM `wms_user_defined_print_solution` AS solution
                WHERE solution.`tenant_id` = @tenantId
                  AND solution.`vue_path` = @vuePath
                  AND solution.`tab_page` = @tabPage;
                """, new { tenantId, vuePath, tabPage })).AsList();
        }

        public async Task<int> AddAsync(PrintSolutionData row)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var id = await connection.ExecuteScalarAsync<int>("""
                    INSERT INTO `wms_user_defined_print_solution`
                        (`vue_path`, `tab_page`, `solution_name`, `config_json`,
                         `report_length`, `report_width`, `report_direction`,
                         `last_update_time`, `tenant_id`)
                    VALUES
                        (@vue_path, @tab_page, @solution_name, @config_json,
                         @report_length, @report_width, @report_direction,
                         @last_update_time, @tenant_id);
                    SELECT LAST_INSERT_ID();
                    """, row, transaction);
                await transaction.CommitAsync();
                return id;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<PrintSolutionWriteStatus> UpdateAsync(PrintSolutionData row)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var exists = await connection.ExecuteScalarAsync<bool>("""
                    SELECT EXISTS(
                        SELECT 1
                        FROM `wms_user_defined_print_solution`
                        WHERE `id` = @id
                        FOR UPDATE);
                    """, new { row.id }, transaction);
                if (!exists)
                {
                    await transaction.RollbackAsync();
                    return PrintSolutionWriteStatus.NotFound;
                }

                var affected = await connection.ExecuteAsync("""
                    UPDATE `wms_user_defined_print_solution`
                    SET `vue_path` = @vue_path,
                        `tab_page` = @tab_page,
                        `solution_name` = @solution_name,
                        `config_json` = @config_json,
                        `report_length` = @report_length,
                        `report_width` = @report_width,
                        `report_direction` = @report_direction,
                        `last_update_time` = @last_update_time
                    WHERE `id` = @id;
                    """, row, transaction);
                await transaction.CommitAsync();
                return affected > 0
                    ? PrintSolutionWriteStatus.Succeeded
                    : PrintSolutionWriteStatus.Failed;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var affected = await connection.ExecuteAsync("""
                    DELETE FROM `wms_user_defined_print_solution`
                    WHERE `id` = @id;
                    """, new { id }, transaction);
                await transaction.CommitAsync();
                return affected > 0;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
