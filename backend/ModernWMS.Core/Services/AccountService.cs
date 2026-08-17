using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ModernWMS.Core.Models;
using System.Linq;
using ModernWMS.Core.Utility;
using System.Data;
using Dapper;
using ModernWMS.Core.Database;
using Microsoft.Extensions.Localization;
using ModernWMS.Core.JWT;

namespace ModernWMS.Core.Services
{
    /// <summary>
    /// AccountService
    /// </summary>
    public class AccountService : IAccountService
    {
        private readonly IMySqlConnectionFactory _connectionFactory;
        private readonly IStringLocalizer<ModernWMS.Core.MultiLanguage> _stringLocalizer;

        public AccountService(IMySqlConnectionFactory connectionFactory, IStringLocalizer<ModernWMS.Core.MultiLanguage> stringLocalizer
)
        {
            _connectionFactory = connectionFactory;
            _stringLocalizer = stringLocalizer;
        }

        /// <summary>
        /// login
        /// </summary>
        /// <param name="loginInput"> login params viewmodel</param>
        /// <param name="currentUser"> current user</param>
        /// <returns></returns>
        public async Task<LoginOutputViewModel> Login(LoginInputViewModel loginInput, CurrentUser currentUser)
        {
            string md5_password = Core.Utility.Md5Helper.Md5Encrypt32(loginInput.password);
            await using var connection = await _connectionFactory.OpenConnectionAsync();
            return (await connection.QueryFirstOrDefaultAsync<LoginOutputViewModel>("""
                SELECT
                    user.`id` AS `user_id`,
                    user.`user_num`,
                    user.`user_name`,
                    user.`user_role`,
                    role.`id` AS `userrole_id`,
                    user.`tenant_id`
                FROM `wms_user` AS user
                INNER JOIN `wms_userrole` AS role
                    ON role.`role_name` = user.`user_role`
                    AND role.`tenant_id` = user.`tenant_id`
                WHERE (user.`user_name` = @loginName OR user.`user_num` = @loginName)
                    AND (user.`auth_string` = @md5Password OR user.`auth_string` = @plainPassword)
                LIMIT 1;
                """, new
                {
                    loginName = loginInput.user_name,
                    md5Password = md5_password,
                    plainPassword = loginInput.password
                }))!;
        }
        
        public string HelloWorld ()
        {
            return _stringLocalizer["hello word"];
        }

    }

}
