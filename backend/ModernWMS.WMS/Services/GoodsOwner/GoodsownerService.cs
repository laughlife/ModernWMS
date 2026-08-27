using System.Data;
using System.Globalization;
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
using MySqlConnector;

namespace ModernWMS.WMS.Services;

internal enum GoodsownerWriteStatus
{
    Succeeded,
    Duplicate,
    NotFound,
    Failed
}

internal sealed record GoodsownerAddResult(GoodsownerWriteStatus Status, int Id);

internal sealed record GoodsownerImportResult(
    int Affected,
    IReadOnlySet<string> DuplicateNames);

internal sealed record GoodsownerData(
    int id,
    string goods_owner_name,
    string city,
    string address,
    string manager,
    string contact_tel,
    string creator,
    DateTime create_time,
    DateTime last_update_time,
    bool is_valid);

internal interface IGoodsownerDataSource
{
    Task<(List<GoodsownerData> Rows, int Total)> PageAsync(PageSearch pageSearch);
    Task<List<GoodsownerData>> GetAllAsync();
    Task<GoodsownerData?> GetAsync(int id);
    Task<GoodsownerAddResult> AddAsync(GoodsownerData owner);
    Task<GoodsownerWriteStatus> UpdateAsync(GoodsownerData owner);
    Task<bool> DeleteAsync(int id);
    Task<GoodsownerImportResult> ImportAsync(
        IReadOnlyCollection<GoodsownerData> owners);
}

/// <summary>
/// Goods owner service.
/// </summary>
public class GoodsownerService : BaseService<GoodsownerEntity>, IGoodsownerService
{
    private readonly IGoodsownerDataSource _dataSource;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;

    /// <summary>
    /// Initializes the service with the shared MySQL connection factory.
    /// </summary>
    public GoodsownerService(
        IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer)
        : this(new DapperGoodsownerDataSource(connectionFactory), stringLocalizer)
    {
    }

    internal GoodsownerService(
        IGoodsownerDataSource dataSource,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
    }

    /// <summary>
    /// </summary>
    public async Task<(List<GoodsownerViewModel> data, int totals)> PageAsync(
        PageSearch pageSearch,
        CurrentUser currentUser)
    {
        var (rows, total) = await _dataSource.PageAsync(pageSearch);
        return (rows.Select(ToViewModel).ToList(), total);
    }

    /// <summary>
    /// </summary>
    public async Task<List<GoodsownerViewModel>> GetAllAsync(CurrentUser currentUser) =>
        (await _dataSource.GetAllAsync()).Select(ToViewModel).ToList();

    /// <summary>
    /// Gets one record by id.
    /// </summary>
    public async Task<GoodsownerViewModel> GetAsync(int id)
    {
        var row = await _dataSource.GetAsync(id);
        return row == null ? new GoodsownerViewModel() : ToViewModel(row);
    }

    /// <summary>
    /// Adds one record.
    /// </summary>
    public async Task<(int id, string msg)> AddAsync(
        GoodsownerViewModel viewModel,
        CurrentUser currentUser)
    {
        var now = DateTime.Now;
        var result = await _dataSource.AddAsync(new GoodsownerData(
            0,
            viewModel.goods_owner_name,
            viewModel.city,
            viewModel.address,
            viewModel.manager,
            viewModel.contact_tel,
            currentUser.user_name,
            now,
            now,
            viewModel.is_valid));

        return result.Status switch
        {
            GoodsownerWriteStatus.Duplicate => (0, DuplicateMessage(viewModel.goods_owner_name)),
            GoodsownerWriteStatus.Succeeded when result.Id > 0 =>
                (result.Id, _stringLocalizer["save_success"]),
            _ => (0, _stringLocalizer["save_failed"])
        };
    }

    /// <summary>
    /// </summary>
    public async Task<(bool flag, string msg)> UpdateAsync(GoodsownerViewModel viewModel)
    {
        var result = await _dataSource.UpdateAsync(new GoodsownerData(
            viewModel.id,
            viewModel.goods_owner_name,
            viewModel.city,
            viewModel.address,
            viewModel.manager,
            viewModel.contact_tel,
            viewModel.creator,
            viewModel.create_time,
            DateTime.Now,
            viewModel.is_valid));

        return result switch
        {
            GoodsownerWriteStatus.NotFound => (false, _stringLocalizer["not_exists_entity"]),
            GoodsownerWriteStatus.Duplicate => (false, DuplicateMessage(viewModel.goods_owner_name)),
            GoodsownerWriteStatus.Succeeded => (true, _stringLocalizer["save_success"]),
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
    /// Imports goods owners in one transaction.
    /// </summary>
    public async Task<(bool flag, List<GoodsownerImportViewModel> errorData)> ExcelAsync(
        List<GoodsownerImportViewModel> input,
        CurrentUser currentUser)
    {
        foreach (var item in input)
        {
            item.errorMsg = string.Empty;
        }

        var now = DateTime.Now;
        var rows = input.Select(item => new GoodsownerData(
            0,
            item.goods_owner_name,
            item.city,
            item.address,
            item.manager,
            item.contact_tel,
            currentUser.user_name,
            now,
            now,
            true)).ToList();
        var result = await _dataSource.ImportAsync(rows);

        if (result.DuplicateNames.Count > 0)
        {
            foreach (var item in input.Where(item => result.DuplicateNames.Contains(item.goods_owner_name)))
            {
                item.errorMsg = DuplicateMessage(item.goods_owner_name);
            }

            return (false, input.Where(item => item.errorMsg.Length > 0).ToList());
        }

        return (result.Affected > 0, []);
    }

    private string DuplicateMessage(string ownerName) =>
        string.Format(
            _stringLocalizer["exists_entity"],
            _stringLocalizer["goods_owner_name"],
            ownerName);

    private static GoodsownerViewModel ToViewModel(GoodsownerData row) => new()
    {
        id = row.id,
        goods_owner_name = row.goods_owner_name,
        city = row.city,
        address = row.address,
        manager = row.manager,
        contact_tel = row.contact_tel,
        creator = row.creator,
        create_time = row.create_time,
        last_update_time = row.last_update_time,
        is_valid = row.is_valid
    };

    private sealed class DapperGoodsownerDataSource : IGoodsownerDataSource
    {
        private const string Projection = """
            owner.`id`,
            owner.`goods_owner_name`,
            owner.`city`,
            owner.`address`,
            owner.`manager`,
            owner.`contact_tel`,
            owner.`creator`,
            owner.`create_time`,
            owner.`last_update_time`,
            owner.`is_valid`
            """;

        private static readonly IReadOnlyDictionary<string, SearchField> SearchFields =
            new Dictionary<string, SearchField>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = new("owner.`id`", typeof(int), false),
                ["goods_owner_name"] = new("owner.`goods_owner_name`", typeof(string), true),
                ["city"] = new("owner.`city`", typeof(string), true),
                ["address"] = new("owner.`address`", typeof(string), true),
                ["manager"] = new("owner.`manager`", typeof(string), true),
                ["contact_tel"] = new("owner.`contact_tel`", typeof(string), true),
                ["creator"] = new("owner.`creator`", typeof(string), true),
                ["create_time"] = new("owner.`create_time`", typeof(DateTime), false),
                ["last_update_time"] = new("owner.`last_update_time`", typeof(DateTime), false),
                ["is_valid"] = new("owner.`is_valid`", typeof(bool), false),
            };

        private readonly IMySqlConnectionFactory _connectionFactory;

        public DapperGoodsownerDataSource(IMySqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory
                ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<(List<GoodsownerData> Rows, int Total)> PageAsync(
            PageSearch pageSearch)
        {
            var filter = BuildFilter(pageSearch.searchObjects);
            var whereClause = string.IsNullOrWhiteSpace(filter.Sql) ? string.Empty : $"WHERE {filter.Sql}";
            filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
            filter.Parameters.Add("page_size", pageSearch.pageSize);

            await using var connection = await _connectionFactory.OpenConnectionAsync();
            using var result = await connection.QueryMultipleAsync($"""
                SELECT COUNT(*)
                FROM `wms_goodsowner` AS owner
                {whereClause};

                SELECT {Projection}
                FROM `wms_goodsowner` AS owner
                {whereClause}
                ORDER BY owner.`create_time` DESC
                LIMIT @page_size OFFSET @offset;
                """, filter.Parameters);
            var total = await result.ReadSingleAsync<int>();
            var rows = (await result.ReadAsync<GoodsownerData>()).AsList();
            return (rows, total);
        }

        public async Task<List<GoodsownerData>> GetAllAsync()
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            return (await connection.QueryAsync<GoodsownerData>($"""
                SELECT {Projection}
                FROM `wms_goodsowner` AS owner
                ORDER BY owner.`create_time` DESC;
                """)).AsList();
        }

        public async Task<GoodsownerData?> GetAsync(int id)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            return await connection.QuerySingleOrDefaultAsync<GoodsownerData>($"""
                SELECT {Projection}
                FROM `wms_goodsowner` AS owner
                WHERE owner.`id` = @id
                LIMIT 1;
                """, new { id });
        }

        public async Task<GoodsownerAddResult> AddAsync(GoodsownerData owner)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var duplicate = await connection.ExecuteScalarAsync<bool>("""
                    SELECT EXISTS(
                        SELECT 1
                        FROM `wms_goodsowner`
                        WHERE `goods_owner_name` = @goods_owner_name);
                    """, owner, transaction);
                if (duplicate)
                {
                    await transaction.RollbackAsync();
                    return new GoodsownerAddResult(GoodsownerWriteStatus.Duplicate, 0);
                }

                var id = await InsertAsync(connection, transaction, owner);
                await transaction.CommitAsync();
                return new GoodsownerAddResult(
                    id > 0 ? GoodsownerWriteStatus.Succeeded : GoodsownerWriteStatus.Failed,
                    id);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<GoodsownerWriteStatus> UpdateAsync(GoodsownerData owner)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var original = await connection.QuerySingleOrDefaultAsync<GoodsownerData>($"""
                    SELECT {Projection}
                    FROM `wms_goodsowner` AS owner
                    WHERE owner.`id` = @id
                    FOR UPDATE;
                    """, new { owner.id }, transaction);
                if (original == null)
                {
                    await transaction.RollbackAsync();
                    return GoodsownerWriteStatus.NotFound;
                }

                var parameters = owner;
                var duplicate = await connection.ExecuteScalarAsync<bool>("""
                    SELECT EXISTS(
                        SELECT 1
                        FROM `wms_goodsowner`
                        WHERE `id` <> @id

                          AND `goods_owner_name` = @goods_owner_name);
                    """, parameters, transaction);
                if (duplicate)
                {
                    await transaction.RollbackAsync();
                    return GoodsownerWriteStatus.Duplicate;
                }

                var affected = await connection.ExecuteAsync("""
                    UPDATE `wms_goodsowner`
                    SET `goods_owner_name` = @goods_owner_name,
                        `city` = @city,
                        `address` = @address,
                        `manager` = @manager,
                        `contact_tel` = @contact_tel,
                        `is_valid` = @is_valid,
                        `last_update_time` = @last_update_time
                    WHERE `id` = @id;
                    """, parameters, transaction);
                await transaction.CommitAsync();
                return affected > 0 ? GoodsownerWriteStatus.Succeeded : GoodsownerWriteStatus.Failed;
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
                    DELETE FROM `wms_goodsowner`
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

        public async Task<GoodsownerImportResult> ImportAsync(
            IReadOnlyCollection<GoodsownerData> owners)
        {
            if (owners.Count == 0)
            {
                return new GoodsownerImportResult(0, new HashSet<string>(StringComparer.Ordinal));
            }

            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var names = owners.Select(owner => owner.goods_owner_name)
                    .Distinct(StringComparer.Ordinal).ToArray();
                var duplicates = (await connection.QueryAsync<string>("""
                    SELECT `goods_owner_name`
                    FROM `wms_goodsowner`
                    WHERE `goods_owner_name` IN @names
                    FOR UPDATE;
                    """, new { names }, transaction))
                    .ToHashSet(StringComparer.Ordinal);
                if (duplicates.Count > 0)
                {
                    await transaction.RollbackAsync();
                    return new GoodsownerImportResult(0, duplicates);
                }

                var affected = await connection.ExecuteAsync("""
                    INSERT INTO `wms_goodsowner`
                        (`goods_owner_name`, `city`, `address`, `manager`, `contact_tel`,
                         `creator`, `create_time`, `last_update_time`, `is_valid`)
                    VALUES
                        (@goods_owner_name, @city, @address, @manager, @contact_tel,
                         @creator, @create_time, @last_update_time, @is_valid);
                    """, owners, transaction);
                await transaction.CommitAsync();
                return new GoodsownerImportResult(
                    affected,
                    new HashSet<string>(StringComparer.Ordinal));
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static async Task<int> InsertAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            GoodsownerData owner) =>
            await connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_goodsowner`
                    (`goods_owner_name`, `city`, `address`, `manager`, `contact_tel`,
                     `creator`, `create_time`, `last_update_time`, `is_valid`)
                VALUES
                    (@goods_owner_name, @city, @address, @manager, @contact_tel,
                     @creator, @create_time, @last_update_time, @is_valid);
                SELECT LAST_INSERT_ID();
                """, owner, transaction);

        private static SearchClause BuildFilter(IEnumerable<SearchObject> filters)
        {
            var clauses = new List<string>();
            var parameters = new DynamicParameters();
            foreach (var filter in filters)
            {
                if (string.IsNullOrWhiteSpace(filter.Text)
                    || !SearchFields.TryGetValue(filter.Name, out var field))
                {
                    continue;
                }

                var parameterName = $"filter{clauses.Count}";
                object value = ConvertFilterValue(filter, field.Type);
                switch (filter.Operator)
                {
                    case Operators.Equal:
                        clauses.Add($"{field.Column} = @{parameterName}");
                        break;
                    case Operators.GreaterThan:
                        clauses.Add($"{field.Column} > @{parameterName}");
                        break;
                    case Operators.GreaterThanOrEqual:
                        clauses.Add($"{field.Column} >= @{parameterName}");
                        break;
                    case Operators.LessThan:
                        clauses.Add($"{field.Column} < @{parameterName}");
                        break;
                    case Operators.LessThanOrEqual:
                        clauses.Add($"{field.Column} <= @{parameterName}");
                        break;
                    case Operators.Contains when field.SupportsContains:
                        clauses.Add($"{field.Column} LIKE @{parameterName} ESCAPE '!'");
                        value = $"%{EscapeLike((string)value)}%";
                        break;
                    case Operators.Contains:
                        throw new InvalidOperationException(
                            $"Contains is not supported for search field '{filter.Name}'.");
                    default:
                        continue;
                }

                parameters.Add(parameterName, value);
            }

            return new SearchClause(string.Join(" AND ", clauses), parameters);
        }

        private static object ConvertFilterValue(SearchObject filter, Type type)
        {
            if (type == typeof(DateTime))
            {
                var value = Convert.ToDateTime(filter.Text, CultureInfo.CurrentCulture);
                if (string.Equals(filter.Type, "DATETIMEPICKER", StringComparison.OrdinalIgnoreCase)
                    && filter.Operator is Operators.LessThan or Operators.LessThanOrEqual)
                {
                    return value.Date.AddHours(23).AddMinutes(59).AddSeconds(59);
                }

                return value;
            }

            return Convert.ChangeType(filter.Text, type, CultureInfo.CurrentCulture)!;
        }

        private static string EscapeLike(string value) => value
            .Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal);

        private sealed record SearchField(string Column, Type Type, bool SupportsContains);
        private sealed record SearchClause(string Sql, DynamicParameters Parameters);
    }
}
