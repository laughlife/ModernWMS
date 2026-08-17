using System.Data;
using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;

namespace ModernWMS.WMS.Services;

internal enum CompanyWriteStatus
{
    Succeeded,
    Duplicate,
    NotFound,
    Failed
}

internal sealed record CompanyAddResult(CompanyWriteStatus Status, int Id);

internal sealed record CompanyData(
    int id,
    string company_name,
    string city,
    string address,
    string manager,
    string contact_tel,
    DateTime create_time,
    DateTime last_update_time,
    long tenant_id);

internal interface ICompanyDataSource
{
    Task<List<CompanyData>> GetAllAsync(long tenantId);
    Task<CompanyData?> GetAsync(int id);
    Task<CompanyAddResult> AddAsync(CompanyData company);
    Task<CompanyWriteStatus> UpdateAsync(CompanyData company);
    Task<bool> DeleteAsync(int id);
}

/// <summary>
/// Company service.
/// </summary>
public class CompanyService : BaseService<CompanyEntity>, ICompanyService
{
    private readonly ICompanyDataSource _dataSource;
    private readonly IStringLocalizer<Core.MultiLanguage> _stringLocalizer;

    /// <summary>
    /// Initializes the company service with the shared MySQL connection factory.
    /// </summary>
    public CompanyService(
        IMySqlConnectionFactory connectionFactory,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer)
        : this(new DapperCompanyDataSource(connectionFactory), stringLocalizer)
    {
    }

    internal CompanyService(
        ICompanyDataSource dataSource,
        IStringLocalizer<Core.MultiLanguage> stringLocalizer)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
    }

    /// <summary>
    /// Get all records for the current tenant.
    /// </summary>
    public async Task<List<CompanyViewModel>> GetAllAsync(CurrentUser currentUser) =>
        (await _dataSource.GetAllAsync(currentUser.tenant_id)).Select(ToViewModel).ToList();

    /// <summary>
    /// Get a record by id.
    /// </summary>
    public async Task<CompanyViewModel> GetAsync(int id)
    {
        var company = await _dataSource.GetAsync(id);
        return company == null ? new CompanyViewModel() : ToViewModel(company);
    }

    /// <summary>
    /// Add a new record.
    /// </summary>
    public async Task<(int id, string msg)> AddAsync(
        CompanyViewModel viewModel,
        CurrentUser currentUser)
    {
        var now = DateTime.Now;
        var result = await _dataSource.AddAsync(new CompanyData(
            0,
            viewModel.company_name,
            viewModel.city,
            viewModel.address,
            viewModel.manager,
            viewModel.contact_tel,
            now,
            now,
            currentUser.tenant_id));

        return result.Status switch
        {
            CompanyWriteStatus.Duplicate => (0, DuplicateMessage(viewModel.company_name)),
            CompanyWriteStatus.Succeeded when result.Id > 0 =>
                (result.Id, _stringLocalizer["save_success"]),
            _ => (0, _stringLocalizer["save_failed"])
        };
    }

    /// <summary>
    /// Update a record.
    /// </summary>
    public async Task<(bool flag, string msg)> UpdateAsync(CompanyViewModel viewModel)
    {
        var result = await _dataSource.UpdateAsync(new CompanyData(
            viewModel.id,
            viewModel.company_name,
            viewModel.city,
            viewModel.address,
            viewModel.manager,
            viewModel.contact_tel,
            viewModel.create_time,
            DateTime.Now,
            0));

        return result switch
        {
            CompanyWriteStatus.NotFound => (false, _stringLocalizer["not_exists_entity"]),
            CompanyWriteStatus.Duplicate => (false, DuplicateMessage(viewModel.company_name)),
            CompanyWriteStatus.Succeeded => (true, _stringLocalizer["save_success"]),
            _ => (false, _stringLocalizer["save_failed"])
        };
    }

    /// <summary>
    /// Delete a record.
    /// </summary>
    public async Task<(bool flag, string msg)> DeleteAsync(int id)
    {
        var deleted = await _dataSource.DeleteAsync(id);
        return deleted
            ? (true, _stringLocalizer["delete_success"])
            : (false, _stringLocalizer["delete_failed"]);
    }

    private string DuplicateMessage(string companyName) =>
        string.Format(
            _stringLocalizer["exists_entity"],
            _stringLocalizer["company_name"],
            companyName);

    private static CompanyViewModel ToViewModel(CompanyData company) => new()
    {
        id = company.id,
        company_name = company.company_name,
        city = company.city,
        address = company.address,
        manager = company.manager,
        contact_tel = company.contact_tel,
        create_time = company.create_time,
        last_update_time = company.last_update_time
    };

    private sealed class DapperCompanyDataSource : ICompanyDataSource
    {
        private const string Projection = """
            `id`,
            `company_name`,
            `city`,
            `address`,
            `manager`,
            `contact_tel`,
            `create_time`,
            `last_update_time`,
            `tenant_id`
            """;

        private readonly IMySqlConnectionFactory _connectionFactory;

        public DapperCompanyDataSource(IMySqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory
                ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<List<CompanyData>> GetAllAsync(long tenantId)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            return (await connection.QueryAsync<CompanyData>($"""
                SELECT {Projection}
                FROM `wms_company`
                WHERE `tenant_id` = @tenantId
                ORDER BY `create_time` DESC;
                """, new { tenantId })).AsList();
        }

        public async Task<CompanyData?> GetAsync(int id)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            return await connection.QuerySingleOrDefaultAsync<CompanyData>($"""
                SELECT {Projection}
                FROM `wms_company`
                WHERE `id` = @id
                LIMIT 1;
                """, new { id });
        }

        public async Task<CompanyAddResult> AddAsync(CompanyData company)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var duplicate = await connection.ExecuteScalarAsync<bool>("""
                    SELECT EXISTS(
                        SELECT 1
                        FROM `wms_company`
                        WHERE `tenant_id` = @tenant_id
                          AND `company_name` = @company_name);
                    """, company, transaction);
                if (duplicate)
                {
                    await transaction.RollbackAsync();
                    return new CompanyAddResult(CompanyWriteStatus.Duplicate, 0);
                }

                var id = await connection.ExecuteScalarAsync<int>("""
                    INSERT INTO `wms_company`
                        (`company_name`, `city`, `address`, `manager`, `contact_tel`,
                         `create_time`, `last_update_time`, `tenant_id`)
                    VALUES
                        (@company_name, @city, @address, @manager, @contact_tel,
                         @create_time, @last_update_time, @tenant_id);
                    SELECT LAST_INSERT_ID();
                    """, company, transaction);
                await transaction.CommitAsync();
                return new CompanyAddResult(
                    id > 0 ? CompanyWriteStatus.Succeeded : CompanyWriteStatus.Failed,
                    id);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<CompanyWriteStatus> UpdateAsync(CompanyData company)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var tenantId = await connection.QuerySingleOrDefaultAsync<long?>("""
                    SELECT `tenant_id`
                    FROM `wms_company`
                    WHERE `id` = @id
                    FOR UPDATE;
                    """, new { company.id }, transaction);
                if (tenantId == null)
                {
                    await transaction.RollbackAsync();
                    return CompanyWriteStatus.NotFound;
                }

                var parameters = new
                {
                    company.id,
                    company.company_name,
                    company.city,
                    company.address,
                    company.manager,
                    company.contact_tel,
                    company.last_update_time,
                    tenant_id = tenantId.Value
                };
                var duplicate = await connection.ExecuteScalarAsync<bool>("""
                    SELECT EXISTS(
                        SELECT 1
                        FROM `wms_company`
                        WHERE `id` <> @id
                          AND `tenant_id` = @tenant_id
                          AND `company_name` = @company_name);
                    """, parameters, transaction);
                if (duplicate)
                {
                    await transaction.RollbackAsync();
                    return CompanyWriteStatus.Duplicate;
                }

                var affected = await connection.ExecuteAsync("""
                    UPDATE `wms_company`
                    SET `company_name` = @company_name,
                        `city` = @city,
                        `address` = @address,
                        `manager` = @manager,
                        `contact_tel` = @contact_tel,
                        `last_update_time` = @last_update_time
                    WHERE `id` = @id;
                    """, parameters, transaction);
                await transaction.CommitAsync();
                return affected > 0 ? CompanyWriteStatus.Succeeded : CompanyWriteStatus.Failed;
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
                    DELETE FROM `wms_company`
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
