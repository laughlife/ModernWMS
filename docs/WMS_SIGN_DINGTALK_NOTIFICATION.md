# WMS 签收钉钉通知

WMS 发货管理完成签收后，会调用 ERP 内部接口，由 ERP 向库存调度单的 `order_user_id`（对应运营）发送钉钉提醒。

消息提示产品已经签收并产生库存，运营必须进入
`https://www.nyamtn.com/erp/logistics-provider/stock` 配置 FBA 货件号，配置完成后才能继续 FBA 发货。

## 生产配置

ModernWMS 环境变量：

```text
ErpIntegration__WmsSignNotificationUrl=https://www.nyamtn.com/admin-api/internal/wms/stock-move/signed
ErpIntegration__InternalToken=<与 ERP 相同的内部 Token>
```

Ruoyi 环境变量：

```text
ERP_WMS_INTERNAL_TOKEN=<与 WMS 相同的内部 Token>
```

Token 只通过生产环境变量配置，不写入仓库。

## 跨仓幂等与恢复契约

ModernWMS 只向 ERP 发送稳定的 `dispatchNo`。本地先提交整单签收事实，再把通知状态从
`Pending` 或 `Failed` 原子领取为 `Sending`，然后调用 ERP。`Sending` 租约为 10 分钟：
新鲜租约禁止重复发送，超过 10 分钟可重新领取。远端失败写回 `Failed`，不回滚已经提交的
签收事实；即使请求取消，通知完成状态也使用独立数据库完成阶段写回，避免永久停留在
`Sending`。

WMS 在“ERP 已接收、WMS 尚未写回 `Sent`”的崩溃窗口采用 at-least-once 重试。有效幂等由
ERP 落库边界保证：`dispatchNo` 先解析为库存调度单，再生成
`STOCK_MOVE:{stockMoveId}:WMS_DISPATCH_SIGNED:{receiverUserId}` 业务键。ERP 的通知表对
`business_key + deleted` 建立唯一索引；并发重复插入由 `DuplicateKeyException` 转为复用
既有事件。因此 WMS 不需要生成另一个跨系统事件 ID，但不得改变或随机化 `dispatchNo`。

对应代码路径：

- ModernWMS：`backend/ModernWMS.WMS/Services/DispatchWorkflow/DispatchWorkflowService.Outbound.cs`
  负责签收事实、本地通知租约和重试；
  `backend/ModernWMS.WMS/Services/Dispatchlist/DispatchSignNotificationClient.cs` 只发送稳定
  `dispatchNo`。
- ERP 接口：`yudao-module-erp/src/main/java/cn/iocoder/yudao/module/erp/controller/admin/logisticsprovider/stock/LogisticsProviderWmsInternalController.java`。
- ERP 入队、业务键、并发去重和 stale `Sending` 恢复：
  `yudao-module-erp/src/main/java/cn/iocoder/yudao/module/erp/service/logisticsprovider/stock/impl/LogisticsProviderDispatchApprovalNotifyService.java`。
- ERP 恢复 SQL：
  `yudao-module-erp/src/main/java/cn/iocoder/yudao/module/erp/dal/mysql/logisticstrack/TrkShipmentNotificationLogMapper.java`。
- ERP 唯一索引迁移：`sql/mysql/20260724_dispatch_approval_notification_retry.sql`。

## 发布顺序

1. 先在 ERP 数据库执行通知业务键唯一索引迁移，并部署包含内部接口、并发去重和 stale
   `Sending` 恢复的 ERP 版本。
2. 配置并核对 ERP `ERP_WMS_INTERNAL_TOKEN`，确认内部接口可以通过 `dispatchNo` 找到唯一库存调度单。
3. 再执行 ModernWMS 的签收事实/通知状态迁移，配置 WMS URL 与相同 Token。
4. 最后部署 ModernWMS。上线验证必须至少覆盖同一 `dispatchNo` 重复请求、远端失败重试和
   10 分钟 stale `Sending` 恢复，确认 ERP 只保留一个业务通知事件。
