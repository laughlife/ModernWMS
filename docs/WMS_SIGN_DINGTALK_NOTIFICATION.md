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

Token 只通过生产环境变量配置，不写入仓库。ERP 使用通知业务键
`STOCK_MOVE:{stockMoveId}:WMS_DISPATCH_SIGNED:{receiverUserId}` 保证重复请求不会重复发送。

签收数据库更新成功后才调用 ERP；调用失败只记录 WMS 错误日志，不回滚签收。ERP 已写入的通知事件继续使用现有定时重试机制投递。
