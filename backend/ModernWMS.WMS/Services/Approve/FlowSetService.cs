/*
 * date：2023-08-18
 * developer：NoNo
 */

using Dapper;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Entities.ViewModels.Approve;

namespace ModernWMS.WMS.Services
{
    /// <summary>
    /// FlowSet Service
    /// </summary>
    public class FlowSetService : BaseService<FlowSetEntity>
    {
        #region Args

        /// <summary>
        /// Shared ERP/WMS MySQL connection factory.
        /// </summary>
        private readonly IMySqlConnectionFactory _connectionFactory;

        /// <summary>
        /// Localizer Service
        /// </summary>
        private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;

        #endregion Args

        #region constructor

        /// <summary>
        /// FlowSet  constructor
        /// </summary>
        /// <param name="connectionFactory">Shared MySQL connection factory.</param>
        /// <param name="stringLocalizer">Localizer</param>
        public FlowSetService(
            IMySqlConnectionFactory connectionFactory
          , IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer
            )
        {
            this._connectionFactory = connectionFactory;
            this._stringLocalizer = stringLocalizer;
        }

        #endregion constructor

        /// <summary>
        /// get flowset map by id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<FlowSetMapGetViewModel?> GetFlowSetMap(int id)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            var main_data = await connection.QuerySingleOrDefaultAsync<FlowSetMainRow>("""
                SELECT `id`, `menu`
                FROM `wms_flowsetmain`
                WHERE `id` = @id
                LIMIT 1;
                """, new { id });
            if (main_data == null)
            {
                return null;
            }

            const string sql = """
                SELECT
                    `id`, `flowsetmain_id`, `is_origin`, `is_end`,
                    `node_guid`, `node_name`, `prev_node_guid`
                FROM `wms_flowset`
                WHERE `flowsetmain_id` = @flowsetMainId;

                SELECT
                    fsu.`id`, fsu.`flowset_id`, @menu AS `menu`, fsu.`node_guid`,
                    fsu.`user_id`, user.`user_name`
                FROM `wms_flowsetusers` AS fsu
                INNER JOIN `wms_user` AS user ON user.`id` = fsu.`user_id`
                WHERE fsu.`flowsetmain_id` = @flowsetMainId;

                SELECT
                    `id`, `flowset_id`, `scheme_name`, `table_name`, `node_guid`,
                    @menu AS `menu`, `logic`, `c1`, `c2`, `col_label`, `col_name`,
                    `compare`, `condition_group`, `assert_mode`, `formulas`, `content`, `sort`
                FROM `wms_flowsetfilter`
                WHERE `flowsetmain_id` = @flowsetMainId;
                """;

            using var result = await connection.QueryMultipleAsync(
                sql,
                new { flowsetMainId = main_data.id, menu = main_data.menu });
            var flowset_vm = (await result.ReadAsync<FlowSetMapGetViewModel>()).AsList();
            var user_data = (await result.ReadAsync<FlowSetUserViewModel>()).AsList();
            var filter_data = (await result.ReadAsync<FlowSetConditionViewModel>()).AsList();
            foreach (var flowset in flowset_vm)
            {
                flowset.user_list = user_data.Where(t => t.node_guid == flowset.node_guid).ToList();
                flowset.filter_list = filter_data.Where(t => t.node_guid == flowset.node_guid).ToList();
            }
            var flow_list = BuildFlow(flowset_vm);
            return flow_list.FirstOrDefault();
        }

        private sealed class FlowSetMainRow
        {
            /// <summary>流程节点标识。</summary>
            public int id { get; set; }

            /// <summary>流程节点菜单。</summary>
            public string menu { get; set; } = string.Empty;
        }

        /// <summary>
        /// build flow
        /// </summary>
        /// <param name="fsm_lsit">FlowSetMap List</param>
        /// <param name="prev_node">prev node guid</param>
        /// <returns></returns>
        public List<FlowSetMapGetViewModel> BuildFlow(List<FlowSetMapGetViewModel> fsm_lsit, string? prev_node = "")
        {
            List<FlowSetMapGetViewModel> flowNodes = new List<FlowSetMapGetViewModel>();
            foreach (var fsm in fsm_lsit)
            {
                if (fsm.prev_node_guid == prev_node)
                {
                    var childNodes = BuildFlow(fsm_lsit, fsm.prev_node_guid);
                    fsm.children = childNodes;
                    flowNodes.Add(fsm);
                }
            }
            return flowNodes;
        }
    }
}
