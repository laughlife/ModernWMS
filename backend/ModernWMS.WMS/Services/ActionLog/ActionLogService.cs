/*
 * date：2023-08-24
 * developer：NoNo
 */

using Dapper;
using ModernWMS.Core.Database;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.DynamicSearch;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;

namespace ModernWMS.WMS.Services
{
    /// <summary>
    ///  ActionLog Service
    /// </summary>
    public class ActionLogService : BaseService<ActionLogEntity>, IActionLogService
    {
        #region Args

        /// <summary>
        /// The DBContext
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> SearchColumns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "log.`id`",
                ["vue_path"] = "log.`vue_path`",
                ["user_name"] = "log.`user_name`",
                ["action_content"] = "log.`action_content`",
                ["action_time"] = "log.`action_time`"
            };

        private readonly IMySqlConnectionFactory _connectionFactory;

        /// <summary>
        /// Localizer Service
        /// </summary>
        private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;

        #endregion Args

        #region constructor

        /// <summary>
        ///ActionLog  constructor
        /// </summary>
        /// <param name="dBContext">The DBContext</param>
        /// <param name="stringLocalizer">Localizer</param>
        public ActionLogService(
            IMySqlConnectionFactory connectionFactory
          , IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer
            )
        {
            _connectionFactory = connectionFactory;
            this._stringLocalizer = stringLocalizer;
        }

        #endregion constructor

        #region Api

        /// <summary>
        /// add a new log record
        /// </summary>
        /// <returns></returns>
        public async Task<bool> AddLogAsync(string vue_path, string content, CurrentUser currentUser)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            var id = await connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_action_log`
                    (`vue_path`, `user_name`, `action_content`, `action_time`, `tenant_id`)
                VALUES
                    (@vue_path, @user_name, @action_content, @action_time, @tenant_id);
                SELECT LAST_INSERT_ID();
                """, new
                {
                    vue_path,
                    user_name = currentUser.user_name,
                    action_content = content,
                    action_time = DateTime.Now,
                    tenant_id = currentUser.tenant_id
                });
            return id > 0;
        }

        /// <summary>
        /// page search
        /// </summary>
        /// <param name="pageSearch">args</param>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        public async Task<(List<ActionLogViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
        {
            var filter = DapperSearchBuilder.Build(pageSearch.searchObjects, SearchColumns);
            var where = string.IsNullOrWhiteSpace(filter.Sql)
                ? "log.`tenant_id` = @tenant_id"
                : $"log.`tenant_id` = @tenant_id AND {filter.Sql}";
            filter.Parameters.Add("tenant_id", currentUser.tenant_id);
            filter.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
            filter.Parameters.Add("page_size", pageSearch.pageSize);

            await using var connection = await _connectionFactory.OpenConnectionAsync();
            using var result = await connection.QueryMultipleAsync($"""
                SELECT COUNT(*)
                FROM `wms_action_log` AS log
                WHERE {where};

                SELECT
                    log.`id`,
                    log.`vue_path`,
                    log.`user_name`,
                    log.`action_content`,
                    log.`action_time`
                FROM `wms_action_log` AS log
                WHERE {where}
                ORDER BY log.`action_time` DESC
                LIMIT @page_size OFFSET @offset;
                """, filter.Parameters);
            var totals = await result.ReadSingleAsync<int>();
            var list = (await result.ReadAsync<ActionLogViewModel>()).AsList();
            return (list, totals);
        }

        #endregion Api
    }
}
