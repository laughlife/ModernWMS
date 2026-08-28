# 当前工作总结与会话接续

更新时间：2026-08-14 18:20（Asia/Shanghai）

## 下次会话阅读顺序

1. 本文：恢复本次实际执行结果和当前运行状态。
2. [PROJECT_OVERVIEW.md](PROJECT_OVERVIEW.md)：恢复四仓职责和系统边界。
3. [CROSS_REPOSITORY_CONTRACTS.md](CROSS_REPOSITORY_CONTRACTS.md)：涉及共享数据库、物流状态或任务链路时继续阅读。
4. 开始新操作前，以当前进程、配置、Git 状态和数据库只读查询重新核验本文中的易变事实。

## 本次目标

- 在开发环境启动 `ruoyi-vue-pro`、`yudao-ui-admin-vue3` 和 `ModernWMS`，使 Windows 可以访问。
- 修正 ERP 前端误连公网演示后端的问题。
- 修正 ModernWMS 的开发数据库地址、端口冲突和登录问题。
- 调查供应商发货后物流单号未进入快递100/17TRACK注册流程的问题。
- 查询生产库近期未注册数据，启动 XXL-JOB 注册接口并执行幂等补注册。

## 开发环境启动结论

### ruoyi-vue-pro

- 开发后端使用 `dev` 配置启动，对外端口为 `48080`。
- Maven 本地仓库遵循当前 Maven `settings.xml`；若下次遇到依赖问题，先运行 `mvn help:effective-settings` 核验 `localRepository`，不要依赖本次会话记忆。
- ERP 物流注册客户端的默认/开发调用地址为 `http://localhost:8081/http-job`，注册接口为 `POST /logistics-track/register`。

### yudao-ui-admin-vue3

- 已重新生成前端依赖并使用本地开发环境配置启动。
- 曾出现前端请求 `http://api-dashboard.yudao.iocoder.cn/admin-api/...`，原因是启动时选用了错误环境配置；正确目标应指向本地 Ruoyi 后端 `/admin-api`。
- 下次启动后应在浏览器 Network 中再次确认字典、登录等请求不再访问公网演示域名。

### ModernWMS

- .NET SDK 已安装到与项目 `global.json` 要求匹配的版本，未通过降低项目 SDK 要求规避问题。
- 后端开发数据库主机调整为 `192.168.100.2:3306`，其余数据库参数沿用现有开发配置。
- 因端口冲突，WMS 使用调整后的访问端口；本次可访问地址为 `http://192.168.100.2:21011/login?culture=zh-cn`。
- 登录曾返回 `operation_failed`，处理时需要同时检查后端是否重启、实际加载的环境配置、数据库连通性和认证日志，不能只根据前端响应判断。

> 运行进程和端口属于易变状态。重启 Codex/OMX 后，先用端口监听、进程列表和健康请求重新确认，不要假定仍在运行。

## 物流注册问题调查

### 业务链路

供应商发货后的国内物流链路为：

1. Ruoyi `ErpLogisticsTrackServiceImpl.startTrackingFromShipmentBatch` 创建统一追踪记录和业务关系。
2. Ruoyi `XxlLogisticsTrackRegisterClient` 调用 XXL-JOB：
   `POST http://localhost:8081/http-job/logistics-track/register`。
3. XXL-JOB `HttpJobController` 将请求交给 `LogisticsTrackRegisterServiceImpl`。
4. XXL-JOB 根据 `providerCode` 调用快递100或17TRACK，保存注册尝试、实时查询载荷和轨迹事件。
5. 第三方后续通过生产回调地址推送数据，由 ERP webhook 处理。

关键代码入口：

- `ruoyi-vue-pro/yudao-module-erp/src/main/java/cn/iocoder/yudao/module/erp/service/logisticstrack/ErpLogisticsTrackServiceImpl.java`
- `ruoyi-vue-pro/yudao-module-erp/src/main/java/cn/iocoder/yudao/module/erp/service/logisticstrack/integration/XxlLogisticsTrackRegisterClient.java`
- `xxl-job/xxl-job-executor/xxl-job-executor-springboot/src/main/java/com/xxl/job/executor/controller/HttpJobController.java`
- `xxl-job/xxl-job-executor/xxl-job-executor-springboot/src/main/java/com/xxl/job/executor/biz/logisticstrack/service/impl/LogisticsTrackRegisterServiceImpl.java`

### 已确认根因

- 供应商发货事务已经成功创建 `trk_track` 和关系记录。
- 故障发生时 Ruoyi 调用的本机 `127.0.0.1:8081` 没有可用 XXL-JOB HTTP 执行器。
- 因而生产记录停留在 `NOT_REGISTERED / PENDING`，且 `trk_track_attempt` 数量为零。
- 这证明请求没有到达 XXL-JOB，不是快递100已经注册但 webhook 回传丢失。
- 发货流程对注册异常进行了捕获，物流注册失败不会回滚供应商发货，这是“发货成功但追踪未注册”的形成原因。

### 生产数据核验与恢复

- 已按用户授权对生产库执行只读排查；本文不保存生产数据库密码或连接串。
- 初次查询发现近 7 天有 40 条 `NOT_REGISTERED` 且零注册尝试的数据，必要字段均完整：手机号、承运商、国内段关系、发货批次和服务商均存在。
- 执行恢复期间又有新记录进入候选集合，因此实际逐项处理的集合大于初始 40 条；重复业务关系导致脚本读取到的调用行数也大于唯一物流单数，但接口的状态判断保证重复调用幂等跳过。
- 所有补注册请求均使用 `autoRegister=true`、`forceRetry=false`，避免覆盖已经成功、在途或签收的状态。
- 最终生产库复核结果：近 7 天 `NOT_REGISTERED`、`FAILED_RETRYABLE`、`FAILED_FINAL` 候选均为 0；`WAITING_PUSH / SUCCESS` 为 41 条。

### 指定单号 JYM166003198828

- 来源：供应商发货批次，国内段，服务商 `KUAIDI100`，承运商加运美。
- 补注册前：`NOT_REGISTERED`、无注册尝试、无服务商数据。
- 补注册后：服务商返回 `200 / 提交成功`，注册状态 `WAITING_PUSH`。
- 注册前实时查询补回 7 条历史轨迹，统一追踪状态更新为 `DELIVERED`，最新事件为已签收。
- 该结果再次确认原问题是注册请求未送达 XXL-JOB。

### 批量处理中的异常

- 两个请求短暂出现 MyBatis-Plus 雪花 ID 生成器的 `Clock moved backwards` 异常。
- `JYM166003191406` 虽然 HTTP 响应报错，但数据库复核显示注册已经完成，状态为 `WAITING_PUSH / SUCCESS`。
- `910062263822` 首次未创建尝试，随后按同一幂等条件重试成功，并通过实时查询更新为 `DELIVERED`。
- 这是新的运行风险：宿主机/虚拟化环境时钟回拨可能使雪花 ID 生成失败。后续若再次出现，应记录发生频率并评估统一 ID 生成策略或时钟同步方案。

## XXL-JOB 当前启动方式

为了只恢复物流 HTTP 注册能力、不误触发其他生产任务，XXL-JOB 使用生产数据源并以受限模式启动：

```text
--spring.profiles.active=prod
--xxl.job.executor.enabled=false
--finance.order-settlement.timer.enabled=false
```

已验证：

- HTTP 端口 `8081` 可访问。
- `POST /http-job/logistics-track/register` 已注册；空请求返回“物流单号不能为空”。
- XXL 调度执行器关闭，财务独立定时器关闭。
- 本次会话结束时进程仍在运行，但下次会话必须重新核验。

注意：这不是完整的生产 XXL 调度部署，只是本次补注册所需的受限运行方式。不要在没有核对 admin、Redis、任务清单和副作用前直接开启全部调度。

## 本次变更与安全边界

- 本次物流恢复没有修改四个业务仓库的源代码。
- 生产写入仅限用户明确授权的物流补注册及其正常产生的注册尝试、载荷和轨迹事件。
- 未执行 `git push`、合并、重置或历史重写。
- 临时生产数据库凭据文件和临时查询/恢复程序已经删除。
- 文档禁止补录生产密码、Token、API key 或完整个人信息。

## 下次优先工作

1. 重启后先阅读本文，并检查四个受管仓库的 `git status`，保护已有改动。
2. 核验 Ruoyi、ERP 前端、ModernWMS 前后端和 XXL-JOB 的实际监听端口及健康状态。
3. 设计并实现物流注册的可靠性修复，优先评估：
   - Ruoyi 到 XXL-JOB 的部署地址不能依赖错误的 `localhost` 拓扑；
   - 对 `NOT_REGISTERED` 且无 attempt 的记录增加可观测、幂等的补偿任务；
   - 为注册调用增加明确告警和监控，避免只记录数据库状态；
   - 对雪花 ID 的时钟回拨异常增加监控和可恢复策略。
4. 修改前先补回归测试，跨 Ruoyi/XXL-JOB 追踪 API、数据库状态机和任务职责影响；代码变更后分别在所属仓库提交。

## 下次会话建议开场指令

```text
请先阅读 doc/CURRENT_WORK_SUMMARY_2026-08-14.md、doc/PROJECT_OVERVIEW.md 和相关跨仓契约，检查四仓 Git 状态与当前服务状态，然后继续设计并修复供应商发货后物流注册不可达时的自动补偿和告警机制。
```
