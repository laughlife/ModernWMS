/*
 * date：2022-12-20
 * developer：NoNo
 */

using System.Data;
using System.Text;
using Dapper;
using Mapster;
using ModernWMS.Core.Database;
using ModernWMS.Core.Services;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using ModernWMS.Core.Models;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.Utility;
using ModernWMS.Core.JWT;

namespace ModernWMS.WMS.Services
{
    /// <summary>
    ///  User Service
    /// </summary>
    public class UserService : BaseService<userEntity>, IUserService
    {
        #region Args

        /// <summary>
        /// MySQL connection factory
        /// </summary>
        private readonly IMySqlConnectionFactory _connectionFactory;

        /// <summary>
        /// Localizer Service
        /// </summary>
        private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;

        private const string AdminRoleName = "admin";

        #endregion Args

        #region constructor

        /// <summary>
        ///User  constructor
        /// </summary>
        /// <param name="connectionFactory">MySQL connection factory</param>
        /// <param name="stringLocalizer">Localizer</param>
        /// <summary>初始化用户服务。</summary>
        public UserService(
            IMySqlConnectionFactory connectionFactory
          , IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer
            )
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
        }

        #endregion constructor

        #region Api

        /// <summary>
        /// get select items
        /// </summary>
        /// <param name="currentUser">current user</param>
        /// <returns></returns>
        /// <inheritdoc />
        public async Task<List<FormSelectItem>> GetSelectItemsAsnyc(CurrentUser currentUser)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            return (await connection.QueryAsync<FormSelectItem>("""
                SELECT 'user_role' AS `code`, `role_name` AS `name`, CAST(`id` AS CHAR) AS `value`,
                       'user''s role' AS `comments`
                FROM `wms_userrole`
                WHERE `is_valid` = 1 AND `tenant_id` = @tenantId;
                """, new { tenantId = currentUser.tenant_id })).AsList();
        }

        /// <summary>
        /// page search
        /// </summary>
        /// <param name="pageSearch">args</param>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        /// <inheritdoc />
        public async Task<(List<UserViewModel> data, int totals)> PageAsync(PageSearch pageSearch, CurrentUser currentUser)
        {
            var where = DapperSearchBuilder.Build(pageSearch.searchObjects, UserSearchColumns);
            where.Parameters.Add("tenantId", currentUser.tenant_id);
            where.Parameters.Add("offset", (pageSearch.pageIndex - 1) * pageSearch.pageSize);
            where.Parameters.Add("pageSize", pageSearch.pageSize);
            var predicates = new List<string> { "`tenant_id` = @tenantId" };
            if (!string.IsNullOrWhiteSpace(where.Sql)) predicates.Add(where.Sql);
            if (pageSearch.sqlTitle == "select") predicates.Add("`is_valid` = 1");
            var whereSql = string.Join(" AND ", predicates);
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            using var grid = await connection.QueryMultipleAsync($"""
                SELECT COUNT(*) FROM `wms_user` WHERE {whereSql};
                SELECT {UserColumns} FROM `wms_user`
                WHERE {whereSql}
                ORDER BY `create_time` DESC
                LIMIT @pageSize OFFSET @offset;
                """, where.Parameters);
            var totals = await grid.ReadSingleAsync<int>();
            var list = (await grid.ReadAsync<userEntity>()).AsList();
            return (list.Adapt<List<UserViewModel>>(), totals);
        }

        /// <summary>
        /// Get all records
        /// </summary>
        /// <returns></returns>
        /// <inheritdoc />
        public async Task<List<UserViewModel>> GetAllAsync(CurrentUser currentUser)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            var data = (await connection.QueryAsync<userEntity>($"""
                SELECT {UserColumns} FROM `wms_user` WHERE `tenant_id` = @tenantId;
                """, new { tenantId = currentUser.tenant_id })).AsList();
            return data.Adapt<List<UserViewModel>>();
        }

        /// <summary>
        /// Get a record by id
        /// </summary>
        /// <returns></returns>
        /// <inheritdoc />
        public async Task<UserViewModel?> GetAsync(int id)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            var entity = await connection.QuerySingleOrDefaultAsync<userEntity>($"""
                SELECT {UserColumns} FROM `wms_user` WHERE `id` = @id LIMIT 1;
                """, new { id });
            if (entity == null)
            {
                return null;
            }
            return entity.Adapt<UserViewModel>();
        }

        /// <summary>
        /// add a new record
        /// </summary>
        /// <param name="viewModel">viewmodel</param>
        /// <param name="currentUser">current user</param>
        /// <returns></returns>
        /// <inheritdoc />
        public async Task<(int id, string msg)> AddAsync(UserViewModel viewModel, CurrentUser currentUser)
        {
            var entity = viewModel.Adapt<userEntity>();
            entity.id = 0;
            var new_auth = GetRandomPassword();
            entity.auth_string = Md5Helper.Md5Encrypt32(new_auth);
            entity.create_time = DateTime.Now;
            entity.last_update_time = DateTime.Now;
            entity.tenant_id = currentUser.tenant_id;
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                if (await UserNumberExistsAsync(connection, transaction, entity.user_num, entity.tenant_id))
                    return await RollbackResult((0, string.Format(_stringLocalizer["exists_entity"],
                        _stringLocalizer["user_num"], viewModel.user_num)), transaction);
                entity.id = await InsertUserAsync(connection, transaction, entity);
                await transaction.CommitAsync();
                return entity.id > 0 ? (entity.id, new_auth) : (0, _stringLocalizer["save_failed"]);
            }
            catch { await transaction.RollbackAsync(); throw; }
        }

        /// <summary>
        /// update a record
        /// </summary>
        /// <param name="viewModel">args</param>
        /// <param name="currentUser">currentUser</param>
        /// <returns></returns>
        /// <inheritdoc />
        public async Task<(bool flag, string msg)> UpdateAsync(UserViewModel viewModel, CurrentUser currentUser)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                if (await UserNumberExistsAsync(connection, transaction, viewModel.user_num,
                        currentUser.tenant_id, viewModel.id))
                    return await RollbackResult((false, string.Format(_stringLocalizer["exists_entity"],
                        _stringLocalizer["user_num"], viewModel.user_num)), transaction);
                var entity = await connection.QuerySingleOrDefaultAsync<userEntity>($"""
                    SELECT {UserColumns} FROM `wms_user`
                    WHERE `id` = @id AND `tenant_id` = @tenantId FOR UPDATE;
                    """, new { viewModel.id, tenantId = currentUser.tenant_id }, transaction);
                if (entity == null)
                    return await RollbackResult((false, _stringLocalizer["not_exists_entity"]), transaction);

                var now = DateTime.Now;
                var qty = IsAdminUser(entity)
                    ? await connection.ExecuteAsync("""
                        UPDATE `wms_user` SET `user_num`=@userNum,`is_valid`=1,`last_update_time`=@now
                        WHERE `id`=@id AND `tenant_id`=@tenantId;
                        """, new { userNum = viewModel.user_num, now, viewModel.id, tenantId = currentUser.tenant_id }, transaction)
                    : await connection.ExecuteAsync("""
                        UPDATE `wms_user` SET `user_num`=@userNum,`user_name`=@userName,
                          `contact_tel`=@contactTel,`user_role`=@userRole,`sex`=@sex,
                          `is_valid`=@isValid,`last_update_time`=@now
                        WHERE `id`=@id AND `tenant_id`=@tenantId;
                        """, new { userNum = viewModel.user_num, userName = viewModel.user_name,
                            contactTel = viewModel.contact_tel, userRole = viewModel.user_role,
                            viewModel.sex, isValid = viewModel.is_valid, now, viewModel.id,
                            tenantId = currentUser.tenant_id }, transaction);
                await transaction.CommitAsync();
                return qty > 0
                    ? (true, _stringLocalizer["save_success"])
                    : (false, _stringLocalizer["save_failed"]);
            }
            catch { await transaction.RollbackAsync(); throw; }
        }

        /// <summary>
        /// delete a record
        /// </summary>
        /// <param name="id">id</param>
        /// <returns></returns>
        /// <inheritdoc />
        public async Task<(bool flag, string msg)> DeleteAsync(int id)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var qty = await connection.ExecuteAsync("DELETE FROM `wms_user` WHERE `id` = @id;", new { id }, transaction);
            await transaction.CommitAsync();
            if (qty > 0)
            {
                return (true, _stringLocalizer["delete_success"]);
            }
            else
            {
                return (false, _stringLocalizer["delete_failed"]);
            }
        }

        /// <summary>
        /// import users by excel
        /// </summary>
        /// <param name="datas">excel datas</param>
        /// <param name="currentUser">current user</param>
        /// <returns></returns>
        /// <inheritdoc />
        public async Task<(bool flag, string msg)> ExcelAsync(List<UserExcelImportViewModel> datas, CurrentUser currentUser)
        {
            StringBuilder sb = new StringBuilder();
            var user_num_repeat_excel = datas.GroupBy(t => t.user_num).Select(t => new { user_num = t.Key, cnt = t.Count() }).Where(t => t.cnt > 1).ToList();
            foreach (var repeat in user_num_repeat_excel)
            {
                sb.AppendLine(string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["user_num"], repeat.user_num));
            }
            if (user_num_repeat_excel.Count > 0)
            {
                return (false, sb.ToString());
            }

            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            var userNumbers = datas.Select(t => t.user_num).ToArray();
            var user_num_repeat_exists = userNumbers.Length == 0 ? [] : (await connection.QueryAsync<string>("""
                SELECT `user_num` FROM `wms_user`
                WHERE `tenant_id`=@tenantId AND `user_num` IN @userNumbers FOR UPDATE;
                """, new { tenantId = currentUser.tenant_id, userNumbers }, transaction)).AsList();
            foreach (var repeat in user_num_repeat_exists)
            {
                sb.AppendLine(string.Format(_stringLocalizer["exists_entity"], _stringLocalizer["user_num"], repeat));
            }
            if (user_num_repeat_exists.Count > 0)
            {
                await transaction.RollbackAsync();
                return (false, sb.ToString());
            }

            var entities = datas.Adapt<List<userEntity>>();
            entities.ForEach(t =>
            {
                t.creator = currentUser.user_name;
                t.tenant_id = currentUser.tenant_id;
                t.auth_string = Md5Helper.Md5Encrypt32("pwd123456");
                t.create_time = DateTime.Now;
                t.last_update_time = DateTime.Now;
                t.is_valid = true;
            });
            var res = 0;
            foreach (var entity in entities)
            {
                await InsertUserAsync(connection, transaction, entity);
                res++;
            }
            await transaction.CommitAsync();
            if (res > 0)
            {
                return (true, _stringLocalizer["save_success"]);
            }
            return (false, _stringLocalizer["save_failed"]);
        }

        /// <summary>
        /// reset password
        /// </summary>
        /// <param name="viewModel">viewmodel</param>
        /// <returns></returns>
        /// <inheritdoc />
        public async Task<(bool, string)> ResetPwd(BatchOperationViewModel viewModel)
        {
            var newpassword = GetRandomPassword();
            if (viewModel.id_list.Count == 0) return (false, _stringLocalizer["operation_failed"]);
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var res = await connection.ExecuteAsync("""
                UPDATE `wms_user` SET `auth_string`=@authString,`last_update_time`=@now
                WHERE `id` IN @ids;
                """, new { authString = Md5Helper.Md5Encrypt32(newpassword), now = DateTime.Now,
                    ids = viewModel.id_list }, transaction);
            await transaction.CommitAsync();
            if (res > 0)
            {
                return (true, newpassword);
            }
            return (false, _stringLocalizer["operation_failed"]);
        }

        /// <summary>
        /// change password
        /// </summary>
        /// <param name="viewModel">viewmodel</param>
        /// <returns></returns>
        /// <inheritdoc />
        public async Task<(bool flag, string msg)> ChangePwd(UserChangePwdViewModel viewModel)
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            var entity = await connection.QuerySingleOrDefaultAsync<userEntity>($"""
                SELECT {UserColumns} FROM `wms_user` WHERE `id`=@id FOR UPDATE;
                """, new { viewModel.id }, transaction);
            if (entity == null)
            {
                await transaction.RollbackAsync();
                return (false, _stringLocalizer["not_exists_entity"]);
            }
            if (!entity.auth_string.Equals(viewModel.old_password))
            {
                await transaction.RollbackAsync();
                return (false, _stringLocalizer["old_password"] + _stringLocalizer["is_incorrect"]);
            }
            await connection.ExecuteAsync("UPDATE `wms_user` SET `auth_string`=@password WHERE `id`=@id;",
                new { password = viewModel.new_password, viewModel.id }, transaction);
            await transaction.CommitAsync();
            return (true, _stringLocalizer["save_success"]);
        }

        /// <summary>
        /// register a new tenant
        /// </summary>
        /// <param name="viewModel">viewModel</param>
        /// <returns></returns>
        /// <inheritdoc />
        public async Task<(bool flag, string msg)> Register(RegisterViewModel viewModel)
        {
            var entity = viewModel.Adapt<userEntity>();
            var time = DateTime.Now;
            entity.user_num = entity.user_name;
            entity.id = 0;
            entity.auth_string = viewModel.auth_string;
            entity.create_time = time;
            entity.last_update_time = time;
            entity.email = viewModel.email;
            entity.sex = viewModel.sex;
            entity.is_valid = true;
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                if (await connection.ExecuteScalarAsync<bool>("""
                    SELECT EXISTS(SELECT 1 FROM `wms_user` WHERE `user_num`=@userNum);
                    """, new { userNum = viewModel.user_name }, transaction))
                    return await RollbackResult((false, _stringLocalizer["username_existed"]), transaction);
                entity.id = await InsertUserAsync(connection, transaction, entity);
                if (entity.id <= 0)
                    return await RollbackResult((false, _stringLocalizer["operation_failed"]), transaction);
                var tenant_id = entity.id;

                #region menus

                var menus = new List<MenuEntity>
                {
                    new MenuEntity
                    {
                        menu_name = "companySetting",
                        module = "baseModule",
                        vue_path = "companySetting",
                        vue_path_detail = "",
                        vue_directory = "base/companySetting",
                        sort = 1,
                        tenant_id = tenant_id
                    },
                    new MenuEntity
                    {
                        menu_name = "userRoleSetting",
                        module = "baseModule",
                        vue_path = "userRoleSetting",
                        vue_path_detail = "",
                        vue_directory = "base/userRoleSetting",
                        sort = 2,
                        tenant_id = tenant_id
                    },
                    new MenuEntity
                    {
                        menu_name = "roleMenu",
                        module = "baseModule",
                        vue_path = "roleMenu",
                        vue_path_detail = "",
                        vue_directory = "base/roleMenu",
                        sort = 3,
                        tenant_id = tenant_id
                    },
                    new MenuEntity
                    {
                        menu_name = "userManagement",
                        module = "baseModule",
                        vue_path = "userManagement",
                        vue_path_detail = "",
                        vue_directory = "base/userManagement",
                        sort = 4,
                        tenant_id = tenant_id
                    },
                    new MenuEntity
                    {
                        menu_name = "commodityCategorySetting",
                        module = "baseModule",
                        vue_path = "commodityCategorySetting",
                        vue_path_detail = "",
                        vue_directory = "base/commodityCategorySetting",
                        sort = 5,
                        tenant_id = tenant_id
                    },
                    new MenuEntity
                    {
                        menu_name = "commodityManagement",
                        module = "baseModule",
                        vue_path = "commodityManagement",
                        vue_path_detail = "",
                        vue_directory = "base/commodityManagement",
                        sort = 6,
                        tenant_id = tenant_id
                    },
                    new MenuEntity
                    {
                        menu_name = "supplier",
                        module = "baseModule",
                        vue_path = "supplier",
                        vue_path_detail = "",
                        vue_directory = "base/supplier",
                        sort = 7,
                        tenant_id = tenant_id
                    },
                    new MenuEntity
                    {
                        menu_name = "warehouseSetting",
                        module = "baseModule",
                        vue_path = "warehouseSetting",
                        vue_path_detail = "",
                        vue_directory = "base/warehouseSetting",
                        sort = 8,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "ownerOfCargo",
                        module = "baseModule",
                        vue_path = "ownerOfCargo",
                        vue_path_detail = "",
                        vue_directory = "base/ownerOfCargo",
                        sort = 9,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "freightSetting",
                        module = "baseModule",
                        vue_path = "freightSetting",
                        vue_path_detail = "",
                        vue_directory = "base/freightSetting",
                        sort = 10,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "print",
                        module = "baseModule",
                        vue_path = "print",
                        vue_path_detail = "",
                        vue_directory = "base/print",
                        sort = 12,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "stockManagement",
                        module = "statisticAnalysis ",
                        vue_path = "stockManagement",
                        vue_path_detail = "",
                        vue_directory = "wms/stockManagement",
                        sort = 3,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "saftyStock",
                        module = "statisticAnalysis ",
                        vue_path = "saftyStock",
                        vue_path_detail = "",
                        vue_directory = "statisticAnalysis/saftyStock",
                        sort = 4,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "asnStatistic",
                        module = "statisticAnalysis ",
                        vue_path = "asnStatistic",
                        vue_path_detail = "",
                        vue_directory = "statisticAnalysis/asnStatistic",
                        sort = 5,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "deliveryStatistic",
                        module = "statisticAnalysis ",
                        vue_path = "deliveryStatistic",
                        vue_path_detail = "",
                        vue_directory = "statisticAnalysis/deliveryStatistic",
                        sort = 6,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "stockageStatistic",
                        module = "statisticAnalysis ",
                        vue_path = "stockageStatistic",
                        vue_path_detail = "",
                        vue_directory = "statisticAnalysis/stockageStatistic",
                        sort = 7,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "warehouseProcessing",
                        module = "warehouseWorkingModule",
                        vue_path = "warehouseProcessing",
                        vue_path_detail = "",
                        vue_directory = "warehouseWorking/warehouseProcessing",
                        sort = 4,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "warehouseMove",
                        module = "warehouseWorkingModule",
                        vue_path = "warehouseMove",
                        vue_path_detail = "",
                        vue_directory = "warehouseWorking/warehouseMove",
                        sort = 5,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "warehouseFreeze",
                        module = "warehouseWorkingModule",
                        vue_path = "warehouseFreeze",
                        vue_path_detail = "",
                        vue_directory = "warehouseWorking/warehouseFreeze",
                        sort = 6,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "warehouseAdjust",
                        module = "warehouseWorkingModule",
                        vue_path = "warehouseAdjust",
                        vue_path_detail = "",
                        vue_directory = "warehouseWorking/warehouseAdjust",
                        sort = 7,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "warehouseTaking",
                        module = "warehouseWorkingModule",
                        vue_path = "warehouseTaking",
                        vue_path_detail = "",
                        vue_directory = "warehouseWorking/warehouseTaking",
                        sort = 8,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "stockAsn",
                        module = "",
                        vue_path = "stockAsn",
                        vue_path_detail = "",
                        vue_directory = "wms/stockAsn",
                        sort = 2,
                        tenant_id = tenant_id
                    },new MenuEntity
                    {
                        menu_name = "deliveryManagement",
                        module = "",
                        vue_path = "deliveryManagement",
                        vue_path_detail = "",
                        vue_directory = "deliveryManagement/deliveryManagement",
                        sort = 5,
                        tenant_id = tenant_id
                    }
                    ,new MenuEntity
                    {
                        menu_name = "largeScreen",
                        module = "",
                        vue_path = "largeScreen",
                        vue_path_detail = "",
                        vue_directory = "largeScreen/largeScreen",
                        sort = 6,
                        tenant_id = tenant_id
                    }
                };

                #endregion menus

                entity.tenant_id = tenant_id;
                entity.creator = entity.user_name;
                entity.user_role = "admin";
                var adminrole = new UserroleEntity { is_valid = true, last_update_time = time, create_time = time, role_name = "admin", tenant_id = tenant_id };
                await connection.ExecuteAsync("""
                    UPDATE `wms_user` SET `tenant_id`=@tenantId,`creator`=@creator,`user_role`=@userRole
                    WHERE `id`=@id;
                    """, new { tenantId = tenant_id, creator = entity.creator, userRole = entity.user_role, entity.id }, transaction);
                adminrole.id = await connection.ExecuteScalarAsync<int>("""
                    INSERT INTO `wms_userrole`
                      (`role_name`,`is_valid`,`create_time`,`last_update_time`,`tenant_id`)
                    VALUES (@role_name,@is_valid,@create_time,@last_update_time,@tenant_id);
                    SELECT LAST_INSERT_ID();
                    """, adminrole, transaction);
                foreach (var menu in menus)
                {
                    menu.id = await connection.ExecuteScalarAsync<int>("""
                        INSERT INTO `wms_menu`
                          (`menu_name`,`module`,`vue_path`,`vue_path_detail`,`vue_directory`,`sort`,`tenant_id`,`menu_actions`)
                        VALUES (@menu_name,@module,@vue_path,@vue_path_detail,@vue_directory,@sort,@tenant_id,@menu_actions);
                        SELECT LAST_INSERT_ID();
                        """, menu, transaction);
                }
                foreach (var menu in menus)
                {
                    await connection.ExecuteAsync("""
                        INSERT INTO `wms_rolemenu`
                          (`userrole_id`,`menu_id`,`authority`,`create_time`,`last_update_time`,`tenant_id`,`menu_actions_authority`)
                        VALUES (@userrole_id,@menu_id,@authority,@create_time,@last_update_time,@tenant_id,@menu_actions_authority);
                        """, new RolemenuEntity {
                        userrole_id = adminrole.id,
                        authority = 1,
                        menu_id = menu.id,
                        tenant_id = tenant_id,
                        last_update_time = time,
                        create_time = time,
                    }, transaction);
                }
                await transaction.CommitAsync();
                return (true, _stringLocalizer["operation_success"]);
            }
            catch { await transaction.RollbackAsync(); throw; }
        }

        /// <summary>
        /// get a random password
        /// </summary>
        /// <returns></returns>
        /// <summary>生成随机密码。</summary>
        public string GetRandomPassword()
        {
            string randomChars = "ABCDEFGHIJKLMNOPQRSTVWXYZ123456789";
            string password = string.Empty;
            int randomNum;
            Random random = new Random();
            for (int i = 0; i < 6; i++)
            {
                randomNum = random.Next(randomChars.Length);
                password += randomChars[randomNum];
            }
            return password;
        }

        private static bool IsAdminUser(userEntity entity)
        {
            return string.Equals(entity.user_role?.Trim(), AdminRoleName, StringComparison.OrdinalIgnoreCase);
        }

        private const string UserColumns = """
            `id`,`user_num`,`user_name`,`contact_tel`,`user_role`,`sex`,`is_valid`,`auth_string`,
            `email`,`creator`,`create_time`,`last_update_time`,`tenant_id`
            """;

        private static readonly IReadOnlyDictionary<string, string> UserSearchColumns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "`id`",
                ["user_num"] = "`user_num`",
                ["user_name"] = "`user_name`",
                ["contact_tel"] = "`contact_tel`",
                ["user_role"] = "`user_role`",
                ["sex"] = "`sex`",
                ["is_valid"] = "`is_valid`",
                ["auth_string"] = "`auth_string`",
                ["email"] = "`email`",
                ["creator"] = "`creator`",
                ["create_time"] = "`create_time`",
                ["last_update_time"] = "`last_update_time`",
                ["tenant_id"] = "`tenant_id`"
            };

        private static Task<bool> UserNumberExistsAsync(IDbConnection connection, IDbTransaction transaction,
            string userNumber, long tenantId, int? excludedId = null) => connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM `wms_user`
                WHERE `tenant_id`=@tenantId AND `user_num`=@userNumber
                  AND (@excludedId IS NULL OR `id`<>@excludedId));
                """, new { tenantId, userNumber, excludedId }, transaction);

        private static Task<int> InsertUserAsync(IDbConnection connection, IDbTransaction transaction,
            userEntity entity) => connection.ExecuteScalarAsync<int>("""
                INSERT INTO `wms_user`
                  (`user_num`,`user_name`,`contact_tel`,`user_role`,`sex`,`is_valid`,`auth_string`,`email`,
                   `creator`,`create_time`,`last_update_time`,`tenant_id`)
                VALUES
                  (@user_num,@user_name,@contact_tel,@user_role,@sex,@is_valid,@auth_string,@email,
                   @creator,@create_time,@last_update_time,@tenant_id);
                SELECT LAST_INSERT_ID();
                """, entity, transaction);

        private static async Task<T> RollbackResult<T>(T result, IDbTransaction transaction)
        {
            if (transaction is System.Data.Common.DbTransaction dbTransaction)
                await dbTransaction.RollbackAsync();
            else
                transaction.Rollback();
            return result;
        }

        #endregion Api
    }
}
