using System.Data;
using System.IdentityModel.Tokens.Jwt;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ModernWMS.Core.Database;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.Core.Utility;

namespace ModernWMS.Core;

/// <summary>
/// 表示 FunctionHelper 类型。
/// </summary>
public class FunctionHelper
{
    private readonly IMySqlConnectionFactory _connectionFactory;
    private readonly IHttpContextAccessor _accessor;
    private readonly IOptions<TokenSettings> _tokenSettings;

    /// <summary>
    /// 初始化 FunctionHelper 的新实例。
    /// </summary>
    public FunctionHelper(IMySqlConnectionFactory connectionFactory,
        IHttpContextAccessor accessor,
        IOptions<TokenSettings> tokenSettings)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _accessor = accessor;
        _tokenSettings = tokenSettings;
    }

    /// <summary>Get the current user information in the token.</summary>
    public CurrentUser GetCurrentUser()
    {
        if (_accessor.HttpContext == null) return new CurrentUser();
        var token = _accessor.HttpContext.Request.Headers["Authorization"].ObjToString();
        if (!token.StartsWith("Bearer")) return new CurrentUser();
        token = token.Replace("Bearer ", "");
        if (token.Length == 0) return new CurrentUser();

        var principal = new JwtSecurityTokenHandler().ValidateToken(token,
            TokenValidationParametersFactory.Create(_tokenSettings.Value), out var securityToken);
        if (securityToken is not JwtSecurityToken jwtSecurityToken ||
            !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            return new CurrentUser();
        return JsonHelper.DeserializeObject<CurrentUser>(
            principal.Claims.First(claim => claim.Type == ClaimValueTypes.Json).Value) ?? new CurrentUser();
    }

    /// <summary>序号表获取单据编号。</summary>
    public async Task<string> GetFormNoAsync(string table_name, string prefix_char = "", ResetRule reset_rule = ResetRule.Day)
    {
        var current_user = GetCurrentUser();
        var nums = await GetFormNoListAsync(table_name, 1, current_user.tenant_id, prefix_char, reset_rule);
        return nums == null ? "" : nums[0];
    }

    /// <summary>序号表批量获取单据编号。</summary>
    public async Task<List<string>> GetFormNoListAsync(string table_name, int Qty = 1, long tenant_id = 1,
        string prefix_char = "", ResetRule reset_rule = ResetRule.Day)
    {
        var nums = new List<string>();
        var resetFormat = reset_rule switch
        {
            ResetRule.Year => "yyyy",
            ResetRule.Month => "yyyyMM",
            _ => "yyyyMMdd"
        };

        await using var connection = await _connectionFactory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // Preserve the legacy global key (tenant_id is metadata, not part of sequence identity).
            // The locking read serializes both existing-row updates and first-row creation.
            var entity = await connection.QueryFirstOrDefaultAsync<GlobalUniqueSerialEntity>("""
                SELECT `id`, `table_name`, `prefix_char`, `reset_rule`, `current_no`,
                       `last_update_time`, `tenant_id`
                FROM `wms_global_unique_serial`
                WHERE `table_name` = @tableName
                  AND `prefix_char` = @prefixChar
                  AND `reset_rule` = @resetFormat
                LIMIT 1 FOR UPDATE;
                """, new { tableName = table_name, prefixChar = prefix_char, resetFormat }, transaction);

            var now = DateTime.Now;
            if (entity == null)
            {
                for (var index = 1; index <= Qty; index++)
                    nums.Add($"{prefix_char}{now.ToString(resetFormat)}-{index.ToString().PadLeft(4, '0')}");

                await connection.ExecuteAsync("""
                    INSERT INTO `wms_global_unique_serial`
                        (`table_name`, `prefix_char`, `reset_rule`, `current_no`, `last_update_time`, `tenant_id`)
                    VALUES
                        (@tableName, @prefixChar, @resetFormat, @currentNo, @now, @tenantId);
                    """, new
                    {
                        tableName = table_name,
                        prefixChar = prefix_char,
                        resetFormat,
                        currentNo = Qty + 1,
                        now,
                        tenantId = tenant_id
                    }, transaction);
            }
            else
            {
                var currentNo = entity.current_no;
                if (!now.ToString(resetFormat).Equals(entity.last_update_time.ToString(resetFormat)))
                    currentNo = 1;
                for (var index = 1; index <= Qty; index++)
                {
                    nums.Add($"{prefix_char}{now.ToString(resetFormat)}-{currentNo.ToString().PadLeft(4, '0')}");
                    currentNo++;
                }
                await connection.ExecuteAsync("""
                    UPDATE `wms_global_unique_serial`
                    SET `current_no` = @currentNo, `last_update_time` = @now
                    WHERE `id` = @id;
                    """, new { currentNo, now, entity.id }, transaction);
            }

            await transaction.CommitAsync();
            return nums;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>重置规则。</summary>
    public enum ResetRule
    {
        /// <summary>
        /// 表示 Year 枚举值。
        /// </summary>
        Year,
        /// <summary>
        /// 表示 Month 枚举值。
        /// </summary>
        Month,
        /// <summary>
        /// 表示 Day 枚举值。
        /// </summary>
        Day
    }
}
