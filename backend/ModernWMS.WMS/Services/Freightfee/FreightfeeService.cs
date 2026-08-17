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

internal enum FreightfeeWriteStatus
{
    Succeeded,
    NotFound,
    Failed
}

internal sealed record FreightfeeAddResult(FreightfeeWriteStatus Status, int Id);

internal sealed record FreightfeeData(
    int id,
    string carrier,
    string departure_city,
    string arrival_city,
    decimal price_per_weight,
    decimal price_per_volume,
    decimal min_payment,
    string creator,
    DateTime create_time,
    DateTime last_update_time,
    bool is_valid,
    long tenant_id);

internal interface IFreightfeeDataSource
{
    Task<(List<FreightfeeData> Rows, int Total)> PageAsync(PageSearch pageSearch, long tenantId);
    Task<List<FreightfeeData>> GetAllAsync(long tenantId);
    Task<FreightfeeData?> GetAsync(int id);
    Task<FreightfeeAddResult> AddAsync(FreightfeeData freightfee);
    Task<FreightfeeWriteStatus> UpdateAsync(FreightfeeData freightfee);
    Task<bool> DeleteAsync(int id);
    Task<int> AddRangeAsync(IReadOnlyCollection<FreightfeeData> freightfees);
}

/// <summary>
/// Freight fee service.
/// </summary>
public class FreightfeeService : BaseService<FreightfeeEntity>, IFreightfeeService
{
    private readonly IFreightfeeDataSource _dataSource;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;

    /// <summary>
    /// Initializes the service with the shared MySQL connection factory.
    /// </summary>
    public FreightfeeService(
        IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer)
        : this(new DapperFreightfeeDataSource(connectionFactory), stringLocalizer)
    {
    }

    internal FreightfeeService(
        IFreightfeeDataSource dataSource,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
    }

    /// <summary>
    /// Page search for the current tenant.
    /// </summary>
    public async Task<(List<FreightfeeViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch,
        CurrentUser currentUser)
    {
        var (rows, total) = await _dataSource.PageAsync(pageSearch, currentUser.tenant_id);
        return (rows.Select(ToViewModel).ToList(), total);
    }

    /// <summary>
    /// Gets all records for the current tenant.
    /// </summary>
    public async Task<List<FreightfeeViewModel>> GetAllAsync(CurrentUser currentUser) =>
        (await _dataSource.GetAllAsync(currentUser.tenant_id)).Select(ToViewModel).ToList();

    /// <summary>
    /// Gets one record by id.
    /// </summary>
    public async Task<FreightfeeViewModel> GetAsync(int id)
    {
        var row = await _dataSource.GetAsync(id);
        return row == null ? null! : ToViewModel(row);
    }

    /// <summary>
    /// Adds one record.
    /// </summary>
    public async Task<(int id, string msg)> AddAsync(
        FreightfeeViewModel viewModel,
        CurrentUser currentUser)
    {
        var now = DateTime.Now;
        var result = await _dataSource.AddAsync(new FreightfeeData(
            0,
            viewModel.carrier,
            viewModel.departure_city,
            viewModel.arrival_city,
            viewModel.price_per_weight,
            viewModel.price_per_volume,
            viewModel.min_payment,
            currentUser.user_name,
            now,
            now,
            viewModel.is_valid,
            currentUser.tenant_id));

        return result.Status == FreightfeeWriteStatus.Succeeded && result.Id > 0
            ? (result.Id, _stringLocalizer["save_success"])
            : (0, _stringLocalizer["save_failed"]);
    }

    /// <summary>
    /// Updates one record without changing its creator, creation time, or tenant.
    /// </summary>
    public async Task<(bool flag, string msg)> UpdateAsync(FreightfeeViewModel viewModel)
    {
        var result = await _dataSource.UpdateAsync(new FreightfeeData(
            viewModel.id,
            viewModel.carrier,
            viewModel.departure_city,
            viewModel.arrival_city,
            viewModel.price_per_weight,
            viewModel.price_per_volume,
            viewModel.min_payment,
            viewModel.creator,
            viewModel.create_time,
            DateTime.Now,
            viewModel.is_valid,
            viewModel.tenant_id));

        return result switch
        {
            FreightfeeWriteStatus.NotFound => (false, _stringLocalizer["not_exists_entity"]),
            FreightfeeWriteStatus.Succeeded => (true, _stringLocalizer["save_success"]),
            _ => (false, _stringLocalizer["save_failed"])
        };
    }

    /// <summary>
    /// Deletes one record by id.
    /// </summary>
    public async Task<(bool flag, string msg)> DeleteAsync(int id)
    {
        var deleted = await _dataSource.DeleteAsync(id);
        return deleted
            ? (true, _stringLocalizer["delete_success"])
            : (false, _stringLocalizer["delete_failed"]);
    }

    /// <summary>
    /// Imports freight fee rows in one transaction.
    /// </summary>
    public async Task<(bool flag, string msg)> ExcelAsync(
        List<FreightfeeExcelmportViewModel> datas,
        CurrentUser currentUser)
    {
        var rows = datas.Select(item =>
        {
            var now = DateTime.Now;
            return new FreightfeeData(
                0,
                item.carrier,
                item.departure_city,
                item.arrival_city,
                item.price_per_weight,
                item.price_per_volume,
                item.min_payment,
                currentUser.user_name,
                now,
                now,
                true,
                currentUser.tenant_id);
        }).ToList();

        var affected = await _dataSource.AddRangeAsync(rows);
        return affected > 0
            ? (true, _stringLocalizer["save_success"])
            : (false, _stringLocalizer["save_failed"]);
    }

    private static FreightfeeViewModel ToViewModel(FreightfeeData row) => new()
    {
        id = row.id,
        carrier = row.carrier,
        departure_city = row.departure_city,
        arrival_city = row.arrival_city,
        price_per_weight = row.price_per_weight,
        price_per_volume = row.price_per_volume,
        min_payment = row.min_payment,
        creator = row.creator,
        create_time = row.create_time,
        last_update_time = row.last_update_time,
        is_valid = row.is_valid,
        tenant_id = row.tenant_id
    };

    private sealed class DapperFreightfeeDataSource : IFreightfeeDataSource
    {
        private const string Projection = """
            fee.`id`,
            fee.`carrier`,
            fee.`departure_city`,
            fee.`arrival_city`,
            fee.`price_per_weight`,
            fee.`price_per_volume`,
            fee.`min_payment`,
            fee.`creator`,
            fee.`create_time`,
            fee.`last_update_time`,
            fee.`is_valid`,
            fee.`tenant_id`
            """;

        private static readonly IReadOnlyDictionary<string, string> SearchColumns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "fee.`id`",
                ["carrier"] = "fee.`carrier`",
                ["departure_city"] = "fee.`departure_city`",
                ["arrival_city"] = "fee.`arrival_city`",
                ["price_per_weight"] = "fee.`price_per_weight`",
                ["price_per_volume"] = "fee.`price_per_volume`",
                ["min_payment"] = "fee.`min_payment`",
                ["creator"] = "fee.`creator`",
                ["create_time"] = "fee.`create_time`",
                ["last_update_time"] = "fee.`last_update_time`",
                ["is_valid"] = "fee.`is_valid`",
                ["tenant_id"] = "fee.`tenant_id`"
            };

        private readonly IMySqlConnectionFactory _connectionFactory;

        public DapperFreightfeeDataSource(IMySqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory
                ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<(List<FreightfeeData> Rows, int Total)> PageAsync(
            PageSearch pageSearch,
            long tenantId)
        {
            var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
            var where = string.IsNullOrWhiteSpace(filter.Sql)
                ? "fee.`tenant_id` = @tenant_id"
                : $"fee.`tenant_id` = @tenant_id AND {filter.Sql}";
            filter.Parameters.Add("tenant_id", tenantId);
            filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
            filter.Parameters.Add("page_size", pageSearch.pageSize);

            await using var connection = await _connectionFactory.OpenConnectionAsync();
            using var result = await connection.QueryMultipleAsync($"""
                SELECT COUNT(*)
                FROM `wms_freightfee` AS fee
                WHERE {where};

                SELECT {Projection}
                FROM `wms_freightfee` AS fee
                WHERE {where}
                ORDER BY fee.`create_time` DESC
                LIMIT @page_size OFFSET @offset;
                """, filter.Parameters);
            var total = await result.ReadSingleAsync<int>();
            var rows = (await result.ReadAsync<FreightfeeData>()).AsList();
            return (rows, total);
        }

        public async Task<List<FreightfeeData>> GetAllAsync(long tenantId)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            return (await connection.QueryAsync<FreightfeeData>($"""
                SELECT {Projection}
                FROM `wms_freightfee` AS fee
                WHERE fee.`tenant_id` = @tenantId;
                """, new { tenantId })).AsList();
        }

        public async Task<FreightfeeData?> GetAsync(int id)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            return await connection.QuerySingleOrDefaultAsync<FreightfeeData>($"""
                SELECT {Projection}
                FROM `wms_freightfee` AS fee
                WHERE fee.`id` = @id
                LIMIT 1;
                """, new { id });
        }

        public async Task<FreightfeeAddResult> AddAsync(FreightfeeData freightfee)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var id = await connection.ExecuteScalarAsync<int>("""
                    INSERT INTO `wms_freightfee`
                        (`carrier`, `departure_city`, `arrival_city`, `price_per_weight`,
                         `price_per_volume`, `min_payment`, `creator`, `create_time`,
                         `last_update_time`, `is_valid`, `tenant_id`)
                    VALUES
                        (@carrier, @departure_city, @arrival_city, @price_per_weight,
                         @price_per_volume, @min_payment, @creator, @create_time,
                         @last_update_time, @is_valid, @tenant_id);
                    SELECT LAST_INSERT_ID();
                    """, freightfee, transaction);
                await transaction.CommitAsync();
                return new FreightfeeAddResult(
                    id > 0 ? FreightfeeWriteStatus.Succeeded : FreightfeeWriteStatus.Failed,
                    id);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<FreightfeeWriteStatus> UpdateAsync(FreightfeeData freightfee)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var exists = await connection.ExecuteScalarAsync<bool>("""
                    SELECT EXISTS(
                        SELECT 1
                        FROM `wms_freightfee`
                        WHERE `id` = @id
                        FOR UPDATE);
                    """, new { freightfee.id }, transaction);
                if (!exists)
                {
                    await transaction.RollbackAsync();
                    return FreightfeeWriteStatus.NotFound;
                }

                var affected = await connection.ExecuteAsync("""
                    UPDATE `wms_freightfee`
                    SET `carrier` = @carrier,
                        `departure_city` = @departure_city,
                        `arrival_city` = @arrival_city,
                        `price_per_weight` = @price_per_weight,
                        `price_per_volume` = @price_per_volume,
                        `min_payment` = @min_payment,
                        `is_valid` = @is_valid,
                        `last_update_time` = @last_update_time
                    WHERE `id` = @id;
                    """, freightfee, transaction);
                await transaction.CommitAsync();
                return affected > 0 ? FreightfeeWriteStatus.Succeeded : FreightfeeWriteStatus.Failed;
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
                    DELETE FROM `wms_freightfee`
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

        public async Task<int> AddRangeAsync(IReadOnlyCollection<FreightfeeData> freightfees)
        {
            if (freightfees.Count == 0)
            {
                return 0;
            }

            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var affected = await connection.ExecuteAsync("""
                    INSERT INTO `wms_freightfee`
                        (`carrier`, `departure_city`, `arrival_city`, `price_per_weight`,
                         `price_per_volume`, `min_payment`, `creator`, `create_time`,
                         `last_update_time`, `is_valid`, `tenant_id`)
                    VALUES
                        (@carrier, @departure_city, @arrival_city, @price_per_weight,
                         @price_per_volume, @min_payment, @creator, @create_time,
                         @last_update_time, @is_valid, @tenant_id);
                    """, freightfees, transaction);
                await transaction.CommitAsync();
                return affected;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
