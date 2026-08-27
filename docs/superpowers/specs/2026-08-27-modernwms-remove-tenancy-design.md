# ModernWMS 全仓取消租户依赖设计

## 背景与目标

ERP 当前没有、也不会启用租户概念。ModernWMS 现有实现仍把 `tenantId`/`tenant_id` 贯穿登录、JWT、API 模型、Dapper SQL、库存分配、预占、幂等键、前端请求和数据库约束，形成了无效的数据边界，并造成与 ERP 当前身份模型不一致。

本次改造必须从 ModernWMS 运行时完全移除租户依赖，同时保留用户、角色、菜单/API 权限、仓库、货主、库区、库位、任务所属人、库存所属人、预占来源和并发版本等真实业务边界。只允许写 `/root/erp/ModernWMS`；其它 ERP 仓库仅作共享契约的只读核验。

## 设计原则

1. 不引入固定租户、默认租户、全局租户、空值回退、`COALESCE` 或改名后的等价租户参数。
2. 运行时代码不得读取、传递、过滤、分组、关联、写入或校验任何租户字段。
3. 删除租户条件后，查询和写入必须依靠真实业务主键、仓库、货主、所属人、任务、库存、分配、预占来源和 CAS 版本保持范围与唯一性。
4. 已发布 Flyway 文件保持不变；结构调整通过新的版本化迁移交付，且本任务不执行实际迁移。
5. 跨仓消费者仍依赖的物理列只作为惰性兼容列保留：允许 `NULL`、无默认值、ModernWMS 不读写，并为无租户查询建立业务索引和唯一键。

## 身份、登录与权限

- 删除 `CurrentUser.tenant_id`、`LoginOutputViewModel.tenant_id`、`userEntity.tenant_id` 在认证链路中的使用以及 `MultiTenancy` 类型和依赖注入。
- 登录查询只按账号/工号、密码和真实角色名称关联 `wms_user`、`wms_userrole`；租户不再参与 SELECT、JOIN 或输出。
- JWT JSON claim 只携带 `user_id`、`user_num`、`user_name`、`user_role`。刷新令牌继续从旧 token 恢复这些身份并校验用户级缓存。
- 角色菜单与角色仓库授权仍按角色 ID、菜单 ID、仓库 ID、有效状态及既有权限规则执行；前端显示不替代后端授权。

## 后端业务改造

### API、DTO 与实体

- 删除生产实体、ViewModel、接口、record 和方法签名中的租户字段或参数。
- 删除默认 `tenant_id=0/1` 和租户相关注释。
- Dapper 映射只选择业务需要的字段，避免因兼容列仍存在而被实体隐式接收。

### 查询、写入与真实边界

- 普通主表按主键和有效状态读取；仓储资源继续按仓库、库区、库位、货主和角色仓库授权收口。
- 任务和库存操作继续校验任务所属人、库存 `order_user_id`、ERP 仓库、SKU、库位分配、reservation/sourceLineKey 和行版本。
- 关联改用明确的父子主键或业务外键，例如 allocation 与 `erp_stock_id`、selection 与任务/明细/allocation、库位与仓库/库区，而不是租户相等条件。
- 写入语句不再包含 `tenant_id`；兼容列由迁移改为可空且无默认值，不能以 0/1 代填。

### 库存分配、预占与幂等

- `StockMutationContext` 删除 `TenantId`；预锁 API 删除 `tenantId` 参数。
- 库存运行模式只以 `erp_warehouse_id` 唯一定位 `wms_inventory_runtime_config`。
- 分配锁与 CAS 使用 allocation ID、ERP stock ID、数量快照、location state 和 row version；错误信息不再使用“跨租户”语义。
- 共享预占头、明细、命令和库位分解继续以 namespace、command ID、reservation 业务类型/ID、sourceLineKey、stock ID、allocation ID、deleted/status/version 为身份。
- operationKey 不包含租户，使用动作、业务类型、任务、明细、库存/分配、数量、版本或命令 ID 形成稳定指纹。数据库唯一键改为全局 operationKey 或完整业务组合。

## 装箱与出库契约

- ERP 与 WMS 继续共用唯一的 PACKING_TASK 预占主单、明细、余额和流水，不生成第二套预占。
- `wms_packing_task_stock_selection` 保留 ACTIVE/CANCELLED/TRANSFERRED、取消人/原因/时间、`row_version` 和 `operation_source`。
- 取消、减少、回退和完成拣货继续分别释放、取消或转移既有绑定，不物理删除审计行。
- 去租户关联后仍必须校验 selection、reservation allocation、allocation、task/item、stock owner、warehouse 和 row version 的一致性。

## 前端

- 删除 `frontend/src` 中租户字段、类型、示例数据、固定值、表单默认值和请求拼装。
- 登录、刷新 token 和业务 API 均不得发送租户字段。
- 页面继续使用后端权限、角色仓库范围、任务与库存所属人规则，不增加前端自判权限。

## 数据库迁移

### 可删除列

对 ModernWMS 独占且无跨仓运行时依赖的 `wms_*` 表，迁移先删除或替换含租户的索引/唯一键，再删除 `tenant_id`。替代约束使用主键、ERP 映射 ID、仓库/库区/库位、货主、业务号、任务、库存分配、操作键和状态等真实业务列。

只读跨仓扫描已经确认，`wms_erp_commodity_map`、`wms_erp_goods_owner_map`、`wms_goodslocation`、`wms_goodsowner`、`wms_warehousearea` 等虽被 Ruoyi 装箱查询读取，但消费者已按真实业务键关联，不读取租户列；其租户列可以在新迁移中删除。

### 暂留兼容列

以下物理列因 ruoyi-vue-pro 运行时仍使用而暂留，统一改为 `NULL`、无默认值，并建立不含租户的等价索引/唯一键：

- `wms_erp_stock_allocation.tenant_id`
- `wms_erp_stock_reservation_allocation.tenant_id`
- `wms_packing_task_stock_selection.tenant_id`
- `wms_erp_stock_allocation_log.tenant_id`
- `wms_inventory_operation.tenant_id`
- `wms_warehouse.tenant_id`
- `trk_stock_reservation.tenant_id`
- `trk_stock_reservation_item.tenant_id`
- `trk_stock_reservation_command.tenant_id`
- `trk_stock_reservation_command_item.tenant_id`

前五列仍由 Ruoyi `PackingTaskMapper` 的兼容 INSERT 写入；`wms_warehouse` 仍被 Ruoyi WMS 模块的 MyBatis 租户拦截链访问；四张 `trk_stock_reservation*` 表仍属于 ERP 共享预占契约。ModernWMS 运行时代码不得因此继续传递或写入这些列。

### 安全前置检查与发布顺序

- 在重建唯一键前，使用只读 SQL 对去掉租户后的业务列分组，确认重复组为零。
- 新迁移包含明确的旧索引删除、新索引创建、列可空/删列顺序和幂等前置检查，不修改历史迁移。
- 发布顺序为：先协调 Ruoyi 对兼容列的后续退出计划；应用 ModernWMS 新 Flyway；再发布去租户 ModernWMS 后端和前端。当前任务只提交迁移文件，不执行数据库变更。

## 测试与验收

实施采用 RED-GREEN-REFACTOR：

1. JWT/CurrentUser 测试证明 token 和刷新身份不含租户。
2. 登录 SQL 集成测试证明用户与角色关联不依赖租户。
3. 装箱列表、可选库存、绑定、减少、取消和运行模式测试在无租户上下文下通过。
4. 库存变更与共享预占测试证明 operationKey、业务唯一性、reservation/sourceLineKey 和 CAS 仍有效。
5. 权限回归覆盖角色菜单、角色仓库、任务所属人和库存所属人边界。
6. 前端单元测试证明请求不发送租户字段。
7. 编译后 SQL 契约扫描证明 `ModernWMS.Core` 与 `ModernWMS.WMS` 生产 Dapper SQL 不包含租户条件、关联或写入。
8. Flyway 契约测试验证新索引、唯一键、兼容列和删列清单。
9. 最终运行后端相关测试与解决方案构建、前端单元测试与生产构建、全仓 `rg` 验收扫描和 `git diff --check`。

## 不在本次范围

- 不修改 ruoyi-vue-pro、yudao-ui-admin-vue3、xxl-job 或 fbashipment-sync 文件。
- 不执行数据库迁移、服务启动/重启、push、merge、reset 或历史重写。
- 不改变菜单/API 权限模型、库存所有权、仓库范围、装箱生命周期或其它无关业务。
