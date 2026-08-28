# 跨仓库契约基线

更新时间：2026-08-14

## 契约变更检查表

任何一项变化都必须追踪生产者和消费者：

- API 路径、HTTP 方法、请求/响应字段、分页和错误码。
- 数据库表、列、类型、默认值、索引、外键/逻辑关联、状态值和数据所有者。
- 权限标识、菜单、身份、数据范围和后端校验。
- XXL handler、Quartz job、调度参数、重试/幂等策略和结果写回。
- Redis key/stream、WebSocket type、消息字段、超时和消费确认。
- 前端字典、状态映射、按钮可见性和页面步骤。

验证顺序：先验证生产者（后端或调度执行器），再验证下游后端消费者，最后验证前端。

## ERP 后端 -> ERP 前端

- 默认 API 前缀：`/admin-api`。
- 前端 API 封装：`yudao-ui-admin-vue3/src/api/`。
- 权限事实源：Ruoyi 后端；前端动态路由与按钮只做展示。
- 采购 `action/status`：字符串，允许小数式状态编码。
- Infra job 页面：`/admin-api/infra/job/*`，归 Ruoyi Quartz，不是 XXL admin 的 `/jobinfo`。

### 供应商在途商品分货记录目的仓（2026-08-19）

- 供应商详情及所有复用 `PurchaseTaskRespVO.task.orderAllocationRecords` 的角色详情接口，每条分货记录新增 `destinationWarehouseNames: string[]`。
- 目的仓事实只从 `erp_purchase_task_allocation_shipment_rel.allocation_id -> shipment_batch_id` 关联仍有效的 `erp_purchase_task_shipment_batch.to_warehouse_name` 组装，不使用任务头仓库、用途或角色字段推断。
- 已取消或已删除发货批次不贡献目的仓；仓名去空、去重并保持关系/批次稳定顺序，无关联时固定返回空数组 `[]`。
- 前端供应商在途“物流明细 → 商品分货记录”逐行展示该字段；多仓分行，空数组显示 `-`。

## ERP <-> XXL-JOB

已发现多种协作方式，不能假设只有一种：

1. Ruoyi HTTP 调用 XXL 执行入口，例如物流追踪注册与 FBA 海外追踪。
2. XXL 读取 Ruoyi 内部配置接口，例如紫鸟 WebDriver 配置。
3. Redis/WebSocket 用于任务命令、进度或结果通知。
4. 某些执行器直接使用 ERP 业务库并写回业务事实。

初始证据：

- `ruoyi-vue-pro/yudao-module-erp/src/main/java/cn/iocoder/yudao/module/erp/service/logisticstrack/integration/XxlLogisticsTrackRegisterClient.java`
- `ruoyi-vue-pro/yudao-module-erp/src/main/java/cn/iocoder/yudao/module/erp/service/logisticsprovider/stock/impl/LogisticsProviderFbaOverseaTrackServiceImpl.java`
- `ruoyi-vue-pro/yudao-module-erp/src/main/java/cn/iocoder/yudao/module/erp/controller/admin/ziniao/webdriver/ZiniaoWebDriverInternalController.java`
- `ruoyi-vue-pro/yudao-module-erp/src/main/java/cn/iocoder/yudao/module/erp/enums/ziniao/ZiniaoWebDriverConstants.java`
- `ruoyi-vue-pro/yudao-server/src/main/resources/application.yaml`
- `xxl-job/xxl-job-admin/src/main/java/com/xxl/job/admin/controller/biz/JobInfoController.java`
- `xxl-job/xxl-job-executor/`

每条任务后续应补齐：触发者、handler/URL、鉴权、payload、状态机、幂等键、重试、超时、结果表/消息、失败补偿和监控。

### 采购发货物流追踪遗漏补偿

- XXL Handler：`logisticsTrackCompensation`，由生产 XXL-JOB Admin 配置调度。
- 扫描来源：ERP 所有的共享表 `trk_logistics_info`；XXL 只读取采购发货上下文并通过既有统一注册服务写 `trk_track*` 追踪事实。
- 默认判定：物流单号产生后按 `COALESCE(shipment_time, create_time)` 等待 2 小时，仍无 `trk_track_event`，且追踪主记录不存在或仍为 `NOT_REGISTERED / FAILED_RETRYABLE` 时进入候选；货拉拉手工确认件排除。
- 幂等参数固定为 `autoRegister=true / forceRetry=false`，不能强制覆盖活动注册、追踪中或终态记录。
- 建议每 15 分钟执行一次，单批默认 100、最大 500；单条失败隔离，批次存在失败时 XXL 任务失败并告警。

## WMS 签收与 FBA 货件号配置提醒

- 触发方：ModernWMS 发货管理 `SignForArrival`，仅在签收状态保存成功后触发。
- ERP 内部接口：`POST /admin-api/internal/wms/stock-move/signed`，请求头 `X-WMS-Internal-Token`，请求体包含 `dispatchNo`。
- 接收人：ERP 根据 `trk_stock_move.no` 定位库存调度单，向 `order_user_id` 对应运营发送钉钉消息。
- 消息动作：提示产品已签收并产生库存，要求进入 `https://www.nyamtn.com/erp/logistics-provider/stock` 配置 FBA 货件号；配置完成前不能继续 FBA 发货。
- 可靠性：ERP 使用 `WMS_DISPATCH_SIGNED` 通知类型和强关联业务键幂等；复用现有通知日志及失败重试。WMS 调用失败不回滚已完成签收，但必须记录错误日志。
- 凭据：两端内部 Token 仅通过环境变量配置，禁止写入仓库。

## ERP <-> ModernWMS

- 两者共享 MySQL 数据库。
- ERP 表由 ERP 所有；ModernWMS 只映射仓储流程需要的最小事实。
- WMS 自有表使用 `wms_` 前缀。
- 已见映射包括 ERP 商品、供应商、采购任务、物流/FBA，以及 `erp_warehouse_id` 和 `wms_erp_*` 表。
- 修改共享字段时，先验证 ERP 的写入语义，再验证 WMS DBContext/迁移/服务，最后验证 WMS 前端。
- 不允许无差别扫描或接管 ERP 全库，也不能把 WMS 业务状态反写成 ERP 主状态，除非契约明确规定。

### 采购货件商品快照数量守恒（2026-08-19）

- `trk_logistics_info.shipment_qty` 与 `product_snapshot_json[].shipmentQty` 合计必须一致；ModernWMS 收货侧保留该一致性校验，不为 ERP 矛盾数据降级。
- ERP 发货批次没有明确 `allocation_shipment_rel`、但 `rawJson.taskItemId` 明确为单商品时，商品快照数量固定取 `erp_purchase_task_shipment_batch.shipment_qty`；查询到的 allocation 只能作为订购人与部门归属回退，不得把任一 `allocation_id / allocation_qty` 伪装成当前批次绑定事实。
- ERP 在新增或更新 `trk_logistics_info` 前执行数量守恒门禁；快照为空、数量缺失、合计溢出或与表头不一致时拒绝写入并记录批次上下文，不修改采购总数，也不以物流单号是否存在改变数量语义。

### 赛狐装箱任务首步只读协作（2026-08-15）

- 事实表由 XXL-JOB 同步链路维护：`ruiyi_sellfox_packing_task` 与 `ruiyi_sellfox_packing_task_item`；ModernWMS 仅做只读最小映射，不反写任务状态。
- XXL Handler 仍为单一外部入口；同步和通知在内部隔离。通知 policy、能力证明及长期 WAITING 刷新默认关闭，未形成 provider snapshot/watermark、历史 fence、source zone 与仓库 readiness 证据前保持 `PREPARED`，不得 claim 或发送。
- 数量缺失与显式 `0` 是不同语义；XXL 落库与 WMS DTO 均须保留 nullable，不得把缺失值零化。
- WMS 首 Tab 由后端 `Features:PackingTaskFirstStep` 和前端 `VITE_PACKING_TASK_FIRST_STEP_ENABLED` 双开关控制，默认均为关闭。启用时必须证明 ERP 仓库 `320118` 有效，且当前租户恰有一条有效 WMS 仓库绑定；否则 fail-closed。
- Ruoyi 阶段 1 已停止旧 Quartz Job、人工 `/sync`、同步 Support 与 Redis writer，仅暂时保留旧表及 `get/page/export-excel` 只读兼容 API。ERP 前端仍可能保留旧写按钮，完整 API/表删除属于后续阶段，须先清理 `yudao-ui-admin-vue3` 消费者。
- 发布顺序：先应用经审核的 expand/upgrade SQL，再部署默认关闭代码，停用 `infra_job` 中旧 Bean 并确认无在途执行，完成数据能力与仓库绑定证明后才能分别开启刷新、WMS 首 Tab 或通知；本次交付未执行任何生产 DDL。
- 变更提交：XXL `18feea27ae16e99955b03151de4c58b8a64612e9`；Ruoyi `e770647d8aaffbff799defda0fbc34d4e07f88a8`；ModernWMS `23b4916be75c062b336807d8712966c82dfe135b`。

## 权限边界

- ERP 权限：Ruoyi System/Security/数据范围。
- WMS 权限：WMS 自有 role/menu/user 流程。
- 两套权限不能因共享数据库而被视为同一体系。跨系统单点登录或统一身份尚未在本次初始化中确认。

## 待完成的契约清单

- ERP/XXL 全部 handler 与路由矩阵。
- Redis Stream、WebSocket 和共享 token 的字段级协议。
- ERP/WMS 表级所有权和读写矩阵。
- 采购、FBA、物流追踪、收货、出库的端到端状态机。
- 菜单、权限点和数据范围的前后端映射。
