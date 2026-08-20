-- 本机开发库受控切换：ERP 唯一库存 + WMS 库位分配 + reservation 所有权。
-- 仅适用于 tenant_id=1 / ERP warehouse_id=320118 的已核对基线。
-- 执行顺序：
-- 1. 完整备份；双方停止库存写入；runtime 必须为 LEGACY_READ + maintenance_enabled=1。
-- 2. 先执行 Ruoyi 20260821_stock_reservation_history_backfill_manual.sql。
-- 3. 再显式调用本过程。本过程成功后仍保持维护状态，不自动切换 CANONICAL_ERP。
-- 4. 双方代码部署并同步开启 ERP 门禁与 CANONICAL_ERP 后，才解除维护。
--
-- 已核对事实：
-- - Ruoyi 回填后仓库三分量必须为 33422 / 2650 / 36072。
-- - 旧 pick 1-4 / 1050 复用 RUOYI owner，不重复 RESERVE。
-- - 独立 WMS 来源为 6 行 / 1600：selection 5-6，pick 5-8。
-- - 本过程完成后必须为 31822 / 4250 / 36072。
-- - WMS_RESERVATION_MIGRATION_V1 namespace 由 ModernWMS 独占重放；Ruoyi 不得用其
--   ReservationOperation 重放该 namespace。双方运行期各自生成新的 commandId，释放/消费只复用 owner id。

SET NAMES utf8mb4;

DELIMITER $$

DROP PROCEDURE IF EXISTS `wms_cutover_reservation_320118_20260821`$$
CREATE PROCEDURE `wms_cutover_reservation_320118_20260821`()
main: BEGIN
    DECLARE v_count BIGINT DEFAULT 0;
    DECLARE v_qty BIGINT DEFAULT 0;
    DECLARE v_available BIGINT DEFAULT 0;
    DECLARE v_occupied BIGINT DEFAULT 0;
    DECLARE v_total BIGINT DEFAULT 0;
    DECLARE v_seq INT DEFAULT 0;
    DECLARE v_max_seq INT DEFAULT 0;
    DECLARE v_source_kind VARCHAR(32);
    DECLARE v_source_row_id BIGINT;
    DECLARE v_task_id BIGINT;
    DECLARE v_task_no VARCHAR(64);
    DECLARE v_source_item_id BIGINT;
    DECLARE v_stock_id BIGINT;
    DECLARE v_allocation_id BIGINT;
    DECLARE v_action_qty BIGINT;
    DECLARE v_reservation_id BIGINT;
    DECLARE v_reservation_item_id BIGINT;
    DECLARE v_reservation_version BIGINT;
    DECLARE v_allocation_reservation_id BIGINT;
    DECLARE v_command_header_id BIGINT;
    DECLARE v_stock_record_id BIGINT;
    DECLARE v_operation_key VARCHAR(64);
    DECLARE v_command_id VARCHAR(128);
    DECLARE v_source_line_key VARCHAR(128);
    DECLARE v_request_fingerprint CHAR(64);
    DECLARE v_result_fingerprint CHAR(64);
    DECLARE v_before_available BIGINT;
    DECLARE v_before_occupied BIGINT;
    DECLARE v_before_total BIGINT;
    DECLARE v_before_allocation_occupied BIGINT;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        DROP TEMPORARY TABLE IF EXISTS tmp_wms_new_owner;
        DROP TEMPORARY TABLE IF EXISTS tmp_old_pick_owner;
        DROP TEMPORARY TABLE IF EXISTS tmp_wms_erp_owner_location;
        DROP TEMPORARY TABLE IF EXISTS tmp_wms_legacy_stock_map;
        RESIGNAL;
    END;

    DROP TEMPORARY TABLE IF EXISTS tmp_wms_new_owner;
    DROP TEMPORARY TABLE IF EXISTS tmp_old_pick_owner;
    DROP TEMPORARY TABLE IF EXISTS tmp_wms_erp_owner_location;
    DROP TEMPORARY TABLE IF EXISTS tmp_wms_legacy_stock_map;

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    START TRANSACTION;

    -- 业务行先锁定，防止 selection/pick 在切换时发生变化。
    SELECT COUNT(*) INTO v_count
      FROM wms_packing_task_stock_selection
     WHERE tenant_id = 1 AND id IN (5, 6)
     FOR UPDATE;
    IF v_count <> 2 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '独立selection必须精确为id5/6两行';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM wms_dispatchpicklist
     WHERE id IN (1,2,3,4,5,6,7,8) AND is_update_stock = 0
     FOR UPDATE;
    IF v_count <> 8 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '待切换pick必须精确为1-8且均未出库';
    END IF;

    -- runtime 是库存结构锁的第一张配置行；缺配置或未进入维护窗口即拒绝。
    SELECT COUNT(*) INTO v_count
      FROM wms_inventory_runtime_config
     WHERE tenant_id = 1 AND erp_warehouse_id = 320118
       AND mode = 'LEGACY_READ' AND maintenance_enabled = 1
     FOR UPDATE;
    IF v_count <> 1 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '仓库必须处于LEGACY_READ维护窗口';
    END IF;

    SELECT COALESCE(SUM(available_qty),0), COALESCE(SUM(occupied_qty),0), COALESCE(SUM(total_qty),0)
      INTO v_available, v_occupied, v_total
      FROM trk_stock
     WHERE warehouse_id = 320118 AND deleted = b'0';
    IF v_available <> 33422 OR v_occupied <> 2650 OR v_total <> 36072 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '必须先完成Ruoyi回填并达到33422/2650/36072';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM wms_erp_stock_allocation allocation
      JOIN trk_stock stock ON stock.id = allocation.erp_stock_id
     WHERE allocation.tenant_id = 1 AND stock.warehouse_id = 320118 AND stock.deleted = b'0';
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '目标仓已存在allocation，拒绝重复切换';
    END IF;

    SELECT COUNT(*) INTO v_count FROM wms_erp_stock_reservation_allocation WHERE tenant_id = 1;
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'WMS reservation allocation必须为空';
    END IF;

    -- 其它旧库存动作仍必须清零；本脚本只保留已精确归因的selection/pick。
    SELECT
      (SELECT COUNT(*)
         FROM wms_stock legacy_stock
         JOIN wms_goodslocation location ON location.id=legacy_stock.goods_location_id
         JOIN wms_warehouse warehouse ON warehouse.id=location.warehouse_id
        WHERE legacy_stock.tenant_id=1 AND warehouse.erp_warehouse_id=320118
          AND legacy_stock.is_freeze=1)
      + (SELECT COUNT(*) FROM wms_stockmove job
           JOIN wms_goodslocation location ON location.id=job.orig_goods_location_id
           JOIN wms_warehouse warehouse ON warehouse.id=location.warehouse_id
          WHERE job.tenant_id=1 AND warehouse.erp_warehouse_id=320118 AND job.move_status=0)
      + (SELECT COUNT(*) FROM wms_stockprocessdetail detail
           JOIN wms_goodslocation location ON location.id=detail.goods_location_id
           JOIN wms_warehouse warehouse ON warehouse.id=location.warehouse_id
          WHERE detail.tenant_id=1 AND warehouse.erp_warehouse_id=320118 AND detail.is_update_stock=0)
      + (SELECT COUNT(*) FROM wms_stocktaking taking
           JOIN wms_goodslocation location ON location.id=taking.goods_location_id
           JOIN wms_warehouse warehouse ON warehouse.id=location.warehouse_id
          WHERE taking.tenant_id=1 AND warehouse.erp_warehouse_id=320118 AND taking.job_status=0)
      + (SELECT COUNT(*) FROM wms_stockadjust adjustment
           JOIN wms_goodslocation location ON location.id=adjustment.goods_location_id
           JOIN wms_warehouse warehouse ON warehouse.id=location.warehouse_id
          WHERE adjustment.tenant_id=1 AND warehouse.erp_warehouse_id=320118
            AND adjustment.is_update_stock=0)
      INTO v_count;
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '仍有冻结/移库/加工/盘点/调整未完成';
    END IF;

    CREATE TEMPORARY TABLE tmp_wms_legacy_stock_map AS
    SELECT legacy_stock.id AS legacy_stock_id,
           MIN(receipt_item.erp_stock_id) AS erp_stock_id,
           COUNT(DISTINCT receipt_item.erp_stock_id) AS erp_stock_count,
           legacy_stock.tenant_id, legacy_stock.sku_id, legacy_stock.goods_location_id,
           location.warehouse_area_id, legacy_stock.goods_owner_id,
           legacy_stock.series_number, legacy_stock.expiry_date, legacy_stock.price,
           DATE(legacy_stock.putaway_date) AS putaway_date, legacy_stock.qty
      FROM wms_stock legacy_stock
      JOIN wms_goodslocation location ON location.id=legacy_stock.goods_location_id
      JOIN wms_warehouse warehouse ON warehouse.id=location.warehouse_id
      LEFT JOIN wms_erp_receipt_item receipt_item
        ON receipt_item.tenant_id=legacy_stock.tenant_id
       AND receipt_item.wms_stock_id=legacy_stock.id
       AND receipt_item.erp_stock_id IS NOT NULL
     WHERE legacy_stock.tenant_id=1 AND legacy_stock.qty>0
       AND warehouse.erp_warehouse_id=320118
     GROUP BY legacy_stock.id,legacy_stock.tenant_id,legacy_stock.sku_id,
              legacy_stock.goods_location_id,location.warehouse_area_id,
              legacy_stock.goods_owner_id,legacy_stock.series_number,
              legacy_stock.expiry_date,legacy_stock.price,DATE(legacy_stock.putaway_date),
              legacy_stock.qty;

    SELECT COUNT(*), COALESCE(SUM(qty),0), COUNT(DISTINCT erp_stock_id)
      INTO v_count, v_qty, v_total
      FROM tmp_wms_legacy_stock_map;
    IF v_count <> 57 OR v_qty <> 35772 OR v_total <> 57 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '旧WMS库存必须精确为57行/57stock/35772';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM tmp_wms_legacy_stock_map map
      LEFT JOIN trk_stock stock ON stock.id=map.erp_stock_id AND stock.deleted=b'0'
      LEFT JOIN wms_goodslocation location ON location.id=map.goods_location_id
      LEFT JOIN wms_warehousearea area ON area.id=map.warehouse_area_id
      LEFT JOIN wms_warehouse warehouse ON warehouse.id=location.warehouse_id
      LEFT JOIN wms_erp_goods_owner_map owner_map
        ON owner_map.tenant_id=1 AND owner_map.wms_goods_owner_id=map.goods_owner_id
       AND owner_map.erp_dept_id <=> stock.dept_id
       AND owner_map.erp_order_user_id <=> stock.order_user_id
      LEFT JOIN wms_erp_commodity_map commodity_map
        ON commodity_map.tenant_id=1 AND commodity_map.erp_commodity_id=stock.commodity_id
     WHERE map.erp_stock_count<>1 OR map.erp_stock_id IS NULL OR map.qty<=0
        OR CHAR_LENGTH(map.series_number)>128
        OR stock.id IS NULL OR stock.warehouse_id<>320118
        OR COALESCE(stock.stock_batch_no,'')<>'POOL'
        OR location.id IS NULL OR location.tenant_id<>1 OR location.is_valid=0
        OR warehouse.id IS NULL OR warehouse.tenant_id<>1 OR warehouse.is_valid=0
        OR warehouse.erp_warehouse_id<>320118
        OR area.id IS NULL OR area.tenant_id<>1 OR area.is_valid=0
        OR area.warehouse_id<>warehouse.id
        OR owner_map.id IS NULL OR commodity_map.id IS NULL
        OR commodity_map.wms_sku_id<>map.sku_id;
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '旧库存ERP/库位/货主/SKU映射不唯一或无效';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM trk_stock stock
      LEFT JOIN (
          SELECT erp_stock_id,SUM(qty) qty FROM tmp_wms_legacy_stock_map GROUP BY erp_stock_id
      ) legacy ON legacy.erp_stock_id=stock.id
     WHERE stock.warehouse_id=320118 AND stock.deleted=b'0'
       AND (stock.total_qty<>stock.available_qty+stock.occupied_qty
            OR stock.available_qty<0 OR stock.occupied_qty<0 OR stock.total_qty<0
            OR COALESCE(legacy.qty,0)>stock.total_qty);
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ERP三分量异常或旧库位数量溢出';
    END IF;

    -- owner 已由 Ruoyi 建立。先锁全部正常 owner/item，再锁目标仓 stock。
    SELECT COUNT(*), COALESCE(SUM(item.remaining_qty),0)
      INTO v_count, v_qty
      FROM trk_stock_reservation reservation
      JOIN trk_stock_reservation_item item
        ON item.tenant_id=reservation.tenant_id AND item.reservation_id=reservation.id
       AND item.deleted=b'0'
      JOIN trk_stock stock ON stock.id=item.stock_id
     WHERE reservation.tenant_id=1 AND reservation.source_system='RUOYI'
       AND reservation.status='ACTIVE' AND reservation.close_mode IS NULL
       AND reservation.deleted=b'0' AND stock.warehouse_id=320118 AND stock.deleted=b'0'
     FOR UPDATE;
    IF v_count <> 9 OR v_qty <> 2650 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'RUOYI正常owner必须精确为9行/2650';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM trk_stock
     WHERE warehouse_id=320118 AND deleted=b'0'
     FOR UPDATE;
    IF v_count <> 58 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '目标仓active ERP stock数量已变化';
    END IF;

    -- 旧库存只迁移位置/货主，不增加ERP总量；一stock一旧位置是本次精确事实。
    INSERT INTO wms_erp_stock_allocation
        (tenant_id,erp_stock_id,warehouse_area_id,goods_location_id,goods_owner_id,
         series_number,expiry_date,price,putaway_date,allocated_qty,occupied_qty,
         location_state,row_version,creator,create_time,updater,update_time)
    SELECT map.tenant_id,map.erp_stock_id,map.warehouse_area_id,map.goods_location_id,
           map.goods_owner_id,map.series_number,map.expiry_date,map.price,map.putaway_date,
           map.qty,stock.occupied_qty,'ACTIVE',0,
           'reservation-cutover',NOW(6),'reservation-cutover',NOW(6)
      FROM tmp_wms_legacy_stock_map map
      JOIN trk_stock stock ON stock.id=map.erp_stock_id;
    IF ROW_COUNT() <> 57 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ACTIVE allocation必须插入57行';
    END IF;

    -- PO2608050004 等只有ERP余额、没有旧WMS位置的数量进入UNLOCATED，绝不伪造库位。
    INSERT INTO wms_erp_stock_allocation
        (tenant_id,erp_stock_id,warehouse_area_id,goods_location_id,goods_owner_id,
         series_number,expiry_date,price,putaway_date,allocated_qty,occupied_qty,
         location_state,row_version,creator,create_time,updater,update_time)
    SELECT 1,stock.id,NULL,NULL,owner_map.wms_goods_owner_id,
           '', '9999-12-31 00:00:00.000000',0,CURRENT_DATE,
           stock.total_qty-COALESCE(mapped.qty,0),stock.occupied_qty,'UNLOCATED',0,
           'reservation-cutover',NOW(6),'reservation-cutover',NOW(6)
      FROM trk_stock stock
      JOIN wms_erp_goods_owner_map owner_map
        ON owner_map.tenant_id=1
       AND owner_map.erp_dept_id <=> stock.dept_id
       AND owner_map.erp_order_user_id <=> stock.order_user_id
      LEFT JOIN (
          SELECT erp_stock_id,SUM(qty) qty FROM tmp_wms_legacy_stock_map GROUP BY erp_stock_id
      ) mapped ON mapped.erp_stock_id=stock.id
     WHERE stock.warehouse_id=320118 AND stock.deleted=b'0'
       AND stock.total_qty>COALESCE(mapped.qty,0);
    IF ROW_COUNT() <> 1 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'UNLOCATED必须精确插入1行';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM wms_erp_stock_allocation allocation
     WHERE allocation.tenant_id=1 AND allocation.location_state IN ('ACTIVE','UNLOCATED')
     FOR UPDATE;
    IF v_count <> 58 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'allocation必须精确为58行';
    END IF;

    INSERT INTO wms_erp_stock_allocation_log
        (tenant_id,operation_key,biz_type,biz_id,biz_item_id,event_type,
         erp_stock_id,allocation_id,counterpart_allocation_id,erp_stock_record_id,
         allocated_delta,occupied_delta,before_allocated_qty,after_allocated_qty,
         before_occupied_qty,after_occupied_qty,operator,operate_time,remark)
    SELECT 1,CONCAT('MWMS:CUTOVER:',allocation.id),'INVENTORY_CUTOVER',320118,allocation.id,
           'BACKFILL',allocation.erp_stock_id,allocation.id,NULL,NULL,
           allocation.allocated_qty,allocation.occupied_qty,0,allocation.allocated_qty,
           0,allocation.occupied_qty,'reservation-cutover',NOW(6),
           '旧WMS库存只迁移位置与货主；occupied来自已认领ERP owner'
      FROM wms_erp_stock_allocation allocation
      JOIN trk_stock stock ON stock.id=allocation.erp_stock_id
     WHERE allocation.tenant_id=1 AND stock.warehouse_id=320118 AND stock.deleted=b'0';
    IF ROW_COUNT() <> 58 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'allocation初始审计必须插入58行';
    END IF;

    -- 把9条RUOYI owner精确分解到其唯一库位，并关联Ruoyi CLAIM_ORPHAN审计命令。
    CREATE TEMPORARY TABLE tmp_wms_erp_owner_location AS
    SELECT item.id reservation_item_id,item.reservation_id,item.stock_id,item.remaining_qty,
           allocation.id allocation_id,command_header.id shared_command_id,
           command_item.stock_record_id
      FROM trk_stock_reservation reservation
      JOIN trk_stock_reservation_item item
        ON item.tenant_id=reservation.tenant_id AND item.reservation_id=reservation.id
       AND item.deleted=b'0'
      JOIN wms_erp_stock_allocation allocation
        ON allocation.tenant_id=1 AND allocation.erp_stock_id=item.stock_id
       AND allocation.location_state='ACTIVE'
      JOIN trk_stock_reservation_command command_header
        ON command_header.tenant_id=1 AND command_header.reservation_id=reservation.id
       AND command_header.action='CLAIM_ORPHAN' AND command_header.result_status='SUCCEEDED'
       AND command_header.deleted=b'0'
      JOIN trk_stock_reservation_command_item command_item
        ON command_item.tenant_id=1 AND command_item.command_header_id=command_header.id
       AND command_item.reservation_item_id=item.id AND command_item.stock_id=item.stock_id
       AND command_item.deleted=b'0'
     WHERE reservation.tenant_id=1 AND reservation.source_system='RUOYI'
       AND reservation.status='ACTIVE' AND reservation.close_mode IS NULL
       AND reservation.deleted=b'0';

    SELECT COUNT(*),COALESCE(SUM(remaining_qty),0)
      INTO v_count,v_qty FROM tmp_wms_erp_owner_location;
    IF v_count<>9 OR v_qty<>2650 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ERP owner库位分解必须精确为9行/2650';
    END IF;

    INSERT INTO wms_erp_stock_reservation_allocation
        (tenant_id,reservation_item_id,erp_stock_id,stock_allocation_id,
         reserved_qty,released_qty,consumed_qty,remaining_qty,status,row_version,
         creator,create_time,updater,update_time,deleted)
    SELECT 1,reservation_item_id,stock_id,allocation_id,
           remaining_qty,0,0,remaining_qty,'ACTIVE',0,
           'reservation-cutover',NOW(6),'reservation-cutover',NOW(6),b'0'
      FROM tmp_wms_erp_owner_location;
    IF ROW_COUNT()<>9 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ERP owner allocation reservation插入失败';
    END IF;

    INSERT INTO wms_erp_stock_allocation_log
        (tenant_id,operation_key,shared_command_id,reservation_id,reservation_item_id,
         biz_type,biz_id,biz_item_id,event_type,erp_stock_id,allocation_id,
         counterpart_allocation_id,erp_stock_record_id,allocated_delta,occupied_delta,
         before_allocated_qty,after_allocated_qty,before_occupied_qty,after_occupied_qty,
         operator,operate_time,remark)
    SELECT 1,CONCAT('MWMS:OWNER:',owner.reservation_item_id),owner.shared_command_id,
           owner.reservation_id,owner.reservation_item_id,'RESERVATION_BACKFILL',320118,
           owner.reservation_item_id,'OWNER_ATTACH',owner.stock_id,owner.allocation_id,NULL,
           owner.stock_record_id,0,0,allocation.allocated_qty,allocation.allocated_qty,
           allocation.occupied_qty,allocation.occupied_qty,
           'reservation-cutover',NOW(6),'RUOYI历史owner只关联库位，不重复RESERVE'
      FROM tmp_wms_erp_owner_location owner
      JOIN wms_erp_stock_allocation allocation ON allocation.id=owner.allocation_id;
    IF ROW_COUNT()<>9 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ERP owner库位审计插入失败';
    END IF;

    -- old pick 1-4 通过dispatch_no->STOCK_MOVE carrier精确复用已有RUOYI owner。
    -- 候选先固化，再证明总数、pick去重数和数量；禁止UPDATE JOIN从多候选中任取。
    CREATE TEMPORARY TABLE tmp_old_pick_owner AS
    SELECT pick.id AS pick_id,pick.pick_qty,reservation.id AS reservation_id,
           item.id AS reservation_item_id,allocation.id AS allocation_id,
           legacy.erp_stock_id
      FROM wms_dispatchpicklist pick
      JOIN wms_dispatchlist dispatch ON dispatch.id=pick.dispatchlist_id
      JOIN trk_stock_move move_job ON move_job.no=dispatch.dispatch_no AND move_job.deleted=b'0'
      JOIN tmp_wms_legacy_stock_map legacy ON legacy.legacy_stock_id=pick.stock_id
      JOIN trk_stock_reservation reservation
        ON reservation.tenant_id=1 AND reservation.source_system='RUOYI'
       AND reservation.carrier_biz_type='STOCK_MOVE' AND reservation.carrier_biz_id=move_job.id
       AND reservation.status='ACTIVE' AND reservation.close_mode IS NULL
       AND reservation.deleted=b'0'
      JOIN trk_stock_reservation_item item
        ON item.tenant_id=1 AND item.reservation_id=reservation.id
       AND item.stock_id=legacy.erp_stock_id AND item.remaining_qty=pick.pick_qty
       AND item.deleted=b'0'
      JOIN wms_erp_stock_allocation allocation
        ON allocation.tenant_id=1 AND allocation.erp_stock_id=legacy.erp_stock_id
       AND allocation.location_state='ACTIVE'
     WHERE pick.id IN (1,2,3,4) AND pick.is_update_stock=0
       AND pick.reservation_id IS NULL AND pick.reservation_item_id IS NULL;

    SELECT COUNT(*),COUNT(DISTINCT pick_id),COALESCE(SUM(pick_qty),0)
      INTO v_count,v_qty,v_total
      FROM tmp_old_pick_owner;
    IF v_count<>4 OR v_qty<>4 OR v_total<>1050 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'old pick1-4必须各自唯一匹配owner/allocation且合计1050';
    END IF;

    UPDATE wms_dispatchpicklist pick
    JOIN tmp_old_pick_owner owner ON owner.pick_id=pick.id
       SET pick.erp_stock_id=owner.erp_stock_id,
           pick.stock_allocation_id=owner.allocation_id,
           pick.reservation_id=owner.reservation_id,
           pick.reservation_item_id=owner.reservation_item_id
     WHERE pick.id IN (1,2,3,4) AND pick.is_update_stock=0
       AND pick.reservation_id IS NULL AND pick.reservation_item_id IS NULL;
    IF ROW_COUNT()<>4 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'old pick1-4 owner引用回填必须为4行';
    END IF;

    SELECT COUNT(*),COALESCE(SUM(pick.pick_qty),0)
      INTO v_count,v_qty
      FROM wms_dispatchpicklist pick
      JOIN wms_dispatchlist dispatch ON dispatch.id=pick.dispatchlist_id
     WHERE pick.id IN (1,2,3,4) AND pick.is_update_stock=0
       AND pick.reservation_id IS NOT NULL AND pick.reservation_item_id IS NOT NULL
       AND ((pick.id=1 AND dispatch.dispatch_no='SM-2026-08-18-013' AND pick.erp_stock_id=1455 AND pick.pick_qty=250)
         OR (pick.id=2 AND dispatch.dispatch_no='SM-2026-08-18-011' AND pick.erp_stock_id=1425 AND pick.pick_qty=250)
         OR (pick.id=3 AND dispatch.dispatch_no='SM-2026-08-18-003' AND pick.erp_stock_id=1452 AND pick.pick_qty=300)
         OR (pick.id=4 AND dispatch.dispatch_no='SM-2026-08-17-004' AND pick.erp_stock_id=1444 AND pick.pick_qty=250));
    IF v_count<>4 OR v_qty<>1050 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'old pick必须精确复用4行/1050';
    END IF;

    -- 建立真正独立的6条WMS来源。pick5-8沿用其永久packing task来源。
    CREATE TEMPORARY TABLE tmp_wms_new_owner (
        seq INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
        source_kind VARCHAR(32) NOT NULL,
        source_row_id BIGINT NOT NULL,
        task_id BIGINT NOT NULL,
        task_no VARCHAR(64) NOT NULL,
        source_item_id BIGINT NOT NULL,
        stock_id BIGINT NOT NULL,
        allocation_id BIGINT NOT NULL,
        action_qty BIGINT NOT NULL,
        source_line_key VARCHAR(128) NOT NULL,
        reservation_id BIGINT NULL,
        reservation_item_id BIGINT NULL,
        UNIQUE KEY uk_tmp_source (source_kind,source_row_id),
        UNIQUE KEY uk_tmp_line (task_id,source_line_key,stock_id)
    ) ENGINE=InnoDB;

    INSERT INTO tmp_wms_new_owner
        (source_kind,source_row_id,task_id,task_no,source_item_id,stock_id,
         allocation_id,action_qty,source_line_key)
    SELECT 'PACKING_SELECTION',selection.id,selection.sellfox_task_id,source_task.packing_task_sn,
           selection.sellfox_item_id,legacy.erp_stock_id,allocation.id,selection.qty,
           CONCAT('PACKING:',selection.sellfox_task_id,':',selection.sellfox_item_id,':',allocation.id)
      FROM wms_packing_task_stock_selection selection
      JOIN ruiyi_sellfox_packing_task source_task
        ON source_task.sellfox_task_id=selection.sellfox_task_id
      JOIN tmp_wms_legacy_stock_map legacy ON legacy.legacy_stock_id=selection.stock_id
      JOIN wms_erp_stock_allocation allocation
        ON allocation.tenant_id=1 AND allocation.erp_stock_id=legacy.erp_stock_id
       AND allocation.location_state='ACTIVE'
     WHERE selection.tenant_id=1 AND selection.id IN (5,6)
    UNION ALL
    SELECT 'PENDING_PICK',pick.id,task.source_task_id,task.source_task_no,
           task_item.source_item_id,legacy.erp_stock_id,allocation.id,pick.pick_qty,
           CONCAT('PACKING:',task.source_task_id,':',task_item.source_item_id,':',allocation.id)
      FROM wms_dispatchpicklist pick
      JOIN wms_dispatch_packing_task_item task_item ON task_item.id=pick.packing_task_item_id
      JOIN wms_dispatch_packing_task task ON task.id=task_item.packing_task_id
      JOIN tmp_wms_legacy_stock_map legacy ON legacy.legacy_stock_id=pick.stock_id
      JOIN wms_erp_stock_allocation allocation
        ON allocation.tenant_id=1 AND allocation.erp_stock_id=legacy.erp_stock_id
       AND allocation.location_state='ACTIVE'
     WHERE pick.id IN (5,6,7,8) AND pick.is_update_stock=0;

    SELECT COUNT(*),COALESCE(SUM(action_qty),0),COUNT(DISTINCT task_id)
      INTO v_count,v_qty,v_total FROM tmp_wms_new_owner;
    IF v_count<>6 OR v_qty<>1600 OR v_total<>5 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '独立WMS来源必须精确为6行/5task/1600';
    END IF;

    SELECT COUNT(*) INTO v_count FROM tmp_wms_new_owner
     WHERE NOT ((source_kind='PACKING_SELECTION' AND source_row_id=5 AND stock_id=1420 AND action_qty=300)
             OR (source_kind='PACKING_SELECTION' AND source_row_id=6 AND stock_id=1420 AND action_qty=200)
             OR (source_kind='PENDING_PICK' AND source_row_id=5 AND stock_id=1488 AND action_qty=500)
             OR (source_kind='PENDING_PICK' AND source_row_id=6 AND stock_id=1427 AND action_qty=100)
             OR (source_kind='PENDING_PICK' AND source_row_id=7 AND stock_id=1464 AND action_qty=250)
             OR (source_kind='PENDING_PICK' AND source_row_id=8 AND stock_id=1468 AND action_qty=250));
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '独立WMS来源事实已变化';
    END IF;

    INSERT INTO trk_stock_reservation
        (reservation_no,source_system,biz_type,biz_id,biz_no,carrier_biz_type,carrier_biz_id,
         status,close_mode,source_snapshot_json,evidence_json,evidence_fingerprint,version,
         creator,create_time,updater,update_time,deleted,tenant_id)
    SELECT CONCAT('MWMS-',LEFT(SHA2(CONCAT('1|MODERN_WMS|PACKING_TASK|',task_id),256),59)),
           'MODERN_WMS','PACKING_TASK',task_id,MIN(task_no),NULL,NULL,'ACTIVE',NULL,
           JSON_OBJECT('taskId',task_id,'taskNo',MIN(task_no)),
           JSON_OBJECT('migration','20260821_WMS_RESERVATION_CUTOVER','rowCount',COUNT(*),'qty',SUM(action_qty)),
           SHA2(CAST(JSON_OBJECT('migration','20260821_WMS_RESERVATION_CUTOVER',
                                 'rowCount',COUNT(*),'qty',SUM(action_qty)) AS CHAR),256),
           0,'reservation-cutover',NOW(),'reservation-cutover',NOW(),b'0',1
      FROM tmp_wms_new_owner GROUP BY task_id;
    IF ROW_COUNT()<>5 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ModernWMS reservation master必须插入5行';
    END IF;

    UPDATE tmp_wms_new_owner source
    JOIN trk_stock_reservation reservation
      ON reservation.tenant_id=1 AND reservation.source_system='MODERN_WMS'
     AND reservation.biz_type='PACKING_TASK' AND reservation.biz_id=source.task_id
     AND reservation.status='ACTIVE' AND reservation.deleted=b'0'
       SET source.reservation_id=reservation.id;

    INSERT INTO trk_stock_reservation_item
        (reservation_id,source_line_type,source_line_id,source_line_key,stock_id,status,
         source_snapshot_json,source_fingerprint,reserved_qty,released_qty,consumed_qty,
         remaining_qty,version,creator,create_time,updater,update_time,deleted,tenant_id)
    SELECT reservation_id,'PACKING_TASK_ITEM',source_item_id,source_line_key,stock_id,'ACTIVE',
           JSON_OBJECT('sourceKind',source_kind,'sourceRowId',source_row_id,'taskId',task_id,
                       'sourceItemId',source_item_id,'stockId',stock_id,'qty',action_qty),
           SHA2(CONCAT('MODERN_WMS|PACKING_TASK|',task_id,'|PACKING_TASK_ITEM|',source_item_id,
                       '|',source_line_key,'|',stock_id),256),
           0,0,0,0,0,'reservation-cutover',NOW(),'reservation-cutover',NOW(),b'0',1
      FROM tmp_wms_new_owner;
    IF ROW_COUNT()<>6 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ModernWMS reservation item必须插入6行';
    END IF;

    UPDATE tmp_wms_new_owner source
    JOIN trk_stock_reservation_item item
      ON item.tenant_id=1 AND item.reservation_id=source.reservation_id
     AND item.source_line_key=source.source_line_key AND item.stock_id=source.stock_id
     AND item.deleted=b'0'
       SET source.reservation_item_id=item.id;

    SELECT COUNT(*) INTO v_count FROM tmp_wms_new_owner
     WHERE reservation_id IS NULL OR reservation_item_id IS NULL;
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ModernWMS owner主键回读失败';
    END IF;

    -- allocation reservation先建立零行；shared/local command随后逐行执行。
    INSERT INTO wms_erp_stock_reservation_allocation
        (tenant_id,reservation_item_id,erp_stock_id,stock_allocation_id,
         reserved_qty,released_qty,consumed_qty,remaining_qty,status,row_version,
         creator,create_time,updater,update_time,deleted)
    SELECT 1,reservation_item_id,stock_id,allocation_id,0,0,0,0,'ACTIVE',0,
           'reservation-cutover',NOW(6),'reservation-cutover',NOW(6),b'0'
      FROM tmp_wms_new_owner;
    IF ROW_COUNT()<>6 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ModernWMS allocation reservation必须插入6行';
    END IF;

    SELECT MAX(seq) INTO v_max_seq FROM tmp_wms_new_owner;
    SET v_seq=1;
    WHILE v_seq<=v_max_seq DO
        SELECT source_kind,source_row_id,task_id,task_no,source_item_id,stock_id,
               allocation_id,action_qty,reservation_id,reservation_item_id,source_line_key
          INTO v_source_kind,v_source_row_id,v_task_id,v_task_no,v_source_item_id,v_stock_id,
               v_allocation_id,v_action_qty,v_reservation_id,v_reservation_item_id,v_source_line_key
          FROM tmp_wms_new_owner WHERE seq=v_seq;

        SELECT id INTO v_allocation_reservation_id
          FROM wms_erp_stock_reservation_allocation
         WHERE tenant_id=1 AND reservation_item_id=v_reservation_item_id
           AND stock_allocation_id=v_allocation_id AND deleted=b'0'
         FOR UPDATE;

        SELECT version INTO v_reservation_version
          FROM trk_stock_reservation
         WHERE tenant_id=1 AND id=v_reservation_id AND status='ACTIVE'
           AND close_mode IS NULL AND deleted=b'0'
         FOR UPDATE;

        SELECT available_qty,occupied_qty,total_qty
          INTO v_before_available,v_before_occupied,v_before_total
          FROM trk_stock WHERE id=v_stock_id AND warehouse_id=320118 AND deleted=b'0'
         FOR UPDATE;
        IF v_before_available<v_action_qty OR v_before_total<>v_before_available+v_before_occupied THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'WMS新预占可用量不足或ERP三分量异常';
        END IF;

        SELECT occupied_qty INTO v_before_allocation_occupied
          FROM wms_erp_stock_allocation
         WHERE id=v_allocation_id AND tenant_id=1 AND erp_stock_id=v_stock_id
           AND location_state='ACTIVE'
         FOR UPDATE;
        IF v_before_allocation_occupied+v_action_qty>
           (SELECT allocated_qty FROM wms_erp_stock_allocation WHERE id=v_allocation_id) THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'WMS新预占超过库位分配数量';
        END IF;

        SET v_command_id=CONCAT('MIGRATE:',v_source_kind,':',v_source_row_id);
        SET v_operation_key=CONCAT('MIG-WMS-RESERVE:',LEFT(SHA2(CONCAT(
            v_source_kind,':',v_source_row_id,':',v_stock_id,':',v_allocation_id,':',v_action_qty),256),48));
        -- 与 StockReservationMutationCoordinator.RequestFingerprint 完全同构；
        -- 迁移 namespace 独立，但不得另造 fingerprint 算法。
        SET v_request_fingerprint=SHA2(CONCAT_WS('|','WMS_RESERVATION_MIGRATION_V1',
            v_command_id,'RESERVE',v_reservation_id,v_reservation_item_id,v_stock_id,
            v_allocation_id,v_action_qty,
            SHA2(CONCAT(v_stock_id,'|',v_allocation_id,'|',v_action_qty),256)),256);

        INSERT INTO trk_stock_reservation_command
            (namespace,command_id,action,reservation_id,request_fingerprint,result_status,
             operator_id,operator_name,version,creator,create_time,updater,update_time,deleted,tenant_id)
        VALUES ('WMS_RESERVATION_MIGRATION_V1',v_command_id,'RESERVE',v_reservation_id,
                v_request_fingerprint,'PENDING',0,'reservation-cutover',0,
                'reservation-cutover',NOW(),'reservation-cutover',NOW(),b'0',1);
        SET v_command_header_id=LAST_INSERT_ID();

        INSERT INTO trk_stock_reservation_command_item
            (command_header_id,line_no,reservation_id,reservation_item_id,source_line_key,
             stock_id,action_qty,expected_reservation_version,expected_item_version,
             allocation_plan_fingerprint,request_line_fingerprint,
             creator,create_time,updater,update_time,deleted,tenant_id)
        VALUES (v_command_header_id,1,v_reservation_id,v_reservation_item_id,v_source_line_key,
                v_stock_id,v_action_qty,v_reservation_version,0,
                SHA2(CONCAT(v_stock_id,'|',v_allocation_id,'|',v_action_qty),256),
                v_request_fingerprint,'reservation-cutover',NOW(),'reservation-cutover',NOW(),b'0',1);

        INSERT INTO wms_inventory_operation
            (tenant_id,operation_key,shared_command_id,reservation_id,reservation_item_id,
             biz_type,biz_id,biz_item_id,mutation_type,erp_stock_id,allocation_id,
             counterpart_allocation_id,quantity,operator,result_status,create_time,update_time)
        VALUES (1,v_operation_key,v_command_header_id,v_reservation_id,v_reservation_item_id,
                'PACKING_LOCK',v_task_id,v_source_item_id,'RESERVE',v_stock_id,v_allocation_id,
                NULL,v_action_qty,'reservation-cutover','PENDING',NOW(6),NOW(6));

        -- 与共享最小事务契约一致：先推进owner，再推进ERP余额与allocation；失败统一回滚。
        UPDATE trk_stock_reservation_item
           SET reserved_qty=v_action_qty,remaining_qty=v_action_qty,status='ACTIVE',version=1,
               updater='reservation-cutover',update_time=NOW()
         WHERE id=v_reservation_item_id AND reservation_id=v_reservation_id
           AND reserved_qty=0 AND released_qty=0 AND consumed_qty=0 AND remaining_qty=0 AND version=0;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'reservation item预占CAS失败';
        END IF;

        UPDATE wms_erp_stock_reservation_allocation
           SET reserved_qty=v_action_qty,remaining_qty=v_action_qty,status='ACTIVE',row_version=1,
               updater='reservation-cutover',update_time=NOW(6)
         WHERE id=v_allocation_reservation_id AND reserved_qty=0 AND released_qty=0
           AND consumed_qty=0 AND remaining_qty=0 AND row_version=0;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'allocation reservation预占CAS失败';
        END IF;

        -- 与运行时代码 RefreshReservationStatusAsync 一致：每个成功命令推进master版本。
        UPDATE trk_stock_reservation
           SET status='ACTIVE',close_mode=NULL,version=version+1,
               updater='reservation-cutover',update_time=NOW()
         WHERE tenant_id=1 AND id=v_reservation_id AND version=v_reservation_version
           AND status='ACTIVE' AND close_mode IS NULL AND deleted=b'0';
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'reservation master版本推进失败';
        END IF;

        UPDATE trk_stock
           SET available_qty=v_before_available-v_action_qty,
               occupied_qty=v_before_occupied+v_action_qty,
               updater='reservation-cutover',update_time=NOW()
         WHERE id=v_stock_id AND available_qty=v_before_available
           AND occupied_qty=v_before_occupied AND total_qty=v_before_total;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ERP库存预占CAS失败';
        END IF;

        UPDATE wms_erp_stock_allocation
           SET occupied_qty=v_before_allocation_occupied+v_action_qty,
               row_version=row_version+1,updater='reservation-cutover',update_time=NOW(6)
         WHERE id=v_allocation_id AND occupied_qty=v_before_allocation_occupied;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'allocation预占CAS失败';
        END IF;

        INSERT INTO trk_stock_record
            (record_no,biz_type,biz_id,biz_item_id,biz_no,stock_id,
             freight_forwarder_id,warehouse_id,dept_id,order_user_id,
             commodity_id,commodity_sku,commodity_name,operation_key,
             reservation_command_id,reservation_id,reservation_item_id,reservation_action,
             available_change_qty,occupied_change_qty,total_change_qty,
             before_available_qty,after_available_qty,before_occupied_qty,after_occupied_qty,
             before_total_qty,after_total_qty,change_qty,before_qty,after_qty,direction,
             operate_time,operator_id,operator_name,remark,
             creator,create_time,updater,update_time,deleted)
        SELECT CONCAT('MIG-WMS-',v_source_kind,'-',v_source_row_id),'PACKING_LOCK',v_task_id,
               v_source_item_id,v_task_no,stock.id,
               stock.freight_forwarder_id,stock.warehouse_id,stock.dept_id,stock.order_user_id,
               stock.commodity_id,stock.commodity_sku,stock.commodity_name,v_operation_key,
               v_command_header_id,v_reservation_id,v_reservation_item_id,'RESERVE',
               -v_action_qty,v_action_qty,0,
               v_before_available,v_before_available-v_action_qty,
               v_before_occupied,v_before_occupied+v_action_qty,
               v_before_total,v_before_total,0,v_before_available,v_before_available-v_action_qty,
               'TRANSFER',NOW(),0,'reservation-cutover','WMS历史永久来源首次写入ERP占用',
               'reservation-cutover',NOW(),'reservation-cutover',NOW(),b'0'
          FROM trk_stock stock WHERE stock.id=v_stock_id;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'WMS RESERVE库存流水插入失败';
        END IF;
        SET v_stock_record_id=LAST_INSERT_ID();
        SET v_result_fingerprint=SHA2(CONCAT(v_request_fingerprint,':',v_stock_record_id,':',v_action_qty),256);

        INSERT INTO wms_erp_stock_allocation_log
            (tenant_id,operation_key,shared_command_id,reservation_id,reservation_item_id,
             biz_type,biz_id,biz_item_id,event_type,erp_stock_id,allocation_id,
             counterpart_allocation_id,erp_stock_record_id,allocated_delta,occupied_delta,
             before_allocated_qty,after_allocated_qty,before_occupied_qty,after_occupied_qty,
             operator,operate_time,remark)
        SELECT 1,v_operation_key,v_command_header_id,v_reservation_id,v_reservation_item_id,
               'PACKING_LOCK',v_task_id,v_source_item_id,'RESERVE',v_stock_id,v_allocation_id,
               NULL,v_stock_record_id,0,v_action_qty,allocation.allocated_qty,allocation.allocated_qty,
               v_before_allocation_occupied,v_before_allocation_occupied+v_action_qty,
               'reservation-cutover',NOW(6),'WMS独立永久来源真实RESERVE'
          FROM wms_erp_stock_allocation allocation WHERE allocation.id=v_allocation_id;

        UPDATE trk_stock_reservation_command_item
           SET stock_record_id=v_stock_record_id,result_remaining_qty=v_action_qty,
               result_line_fingerprint=SHA2(CONCAT(v_command_header_id,'|',
                   v_reservation_item_id,'|',v_stock_record_id,'|',v_action_qty),256),
               updater='reservation-cutover',update_time=NOW()
         WHERE command_header_id=v_command_header_id AND reservation_item_id=v_reservation_item_id;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'shared command item完成失败';
        END IF;

        UPDATE trk_stock_reservation_command
           SET result_status='SUCCEEDED',result_fingerprint=v_result_fingerprint,
               complete_time=NOW(),version=1,updater='reservation-cutover',update_time=NOW()
         WHERE id=v_command_header_id AND result_status='PENDING' AND version=0;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'shared command完成失败';
        END IF;

        UPDATE wms_inventory_operation
           SET result_status='SUCCEEDED',erp_stock_record_id=v_stock_record_id,update_time=NOW(6)
         WHERE tenant_id=1 AND operation_key=v_operation_key AND result_status='PENDING';
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'local command完成失败';
        END IF;

        SET v_seq=v_seq+1;
    END WHILE;

    UPDATE wms_packing_task_stock_selection selection
    JOIN tmp_wms_new_owner source
      ON source.source_kind='PACKING_SELECTION' AND source.source_row_id=selection.id
       SET selection.erp_stock_id=source.stock_id,selection.stock_allocation_id=source.allocation_id,
           selection.reservation_id=source.reservation_id,
           selection.reservation_item_id=source.reservation_item_id
     WHERE selection.tenant_id=1 AND selection.id IN (5,6)
       AND selection.reservation_id IS NULL AND selection.reservation_item_id IS NULL;
    IF ROW_COUNT()<>2 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'selection reservation引用回填失败';
    END IF;

    UPDATE wms_dispatchpicklist pick
    JOIN tmp_wms_new_owner source
      ON source.source_kind='PENDING_PICK' AND source.source_row_id=pick.id
       SET pick.erp_stock_id=source.stock_id,pick.stock_allocation_id=source.allocation_id,
           pick.reservation_id=source.reservation_id,pick.reservation_item_id=source.reservation_item_id
     WHERE pick.id IN (5,6,7,8) AND pick.is_update_stock=0
       AND pick.reservation_id IS NULL AND pick.reservation_item_id IS NULL;
    IF ROW_COUNT()<>4 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'new pick reservation引用回填失败';
    END IF;

    -- 最终四层守恒：ERP stock、shared owner、allocation、location owner。
    SELECT COUNT(*) INTO v_count
      FROM trk_stock stock
      LEFT JOIN (
          SELECT item.stock_id,SUM(item.remaining_qty) remaining_qty
            FROM trk_stock_reservation reservation
            JOIN trk_stock_reservation_item item
              ON item.tenant_id=reservation.tenant_id AND item.reservation_id=reservation.id
             AND item.deleted=b'0'
           WHERE reservation.tenant_id=1 AND reservation.deleted=b'0'
             AND reservation.status IN ('ACTIVE','PARTIALLY_SETTLED')
           GROUP BY item.stock_id
      ) owner ON owner.stock_id=stock.id
     WHERE stock.warehouse_id=320118 AND stock.deleted=b'0'
       AND stock.occupied_qty<>COALESCE(owner.remaining_qty,0);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ERP occupied与shared owner不守恒';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM trk_stock stock
      LEFT JOIN (
          SELECT erp_stock_id,SUM(allocated_qty) allocated_qty,SUM(occupied_qty) occupied_qty
            FROM wms_erp_stock_allocation
           WHERE tenant_id=1 AND location_state IN ('ACTIVE','UNLOCATED')
           GROUP BY erp_stock_id
      ) allocation ON allocation.erp_stock_id=stock.id
     WHERE stock.warehouse_id=320118 AND stock.deleted=b'0'
       AND (COALESCE(allocation.allocated_qty,0)<>stock.total_qty
            OR COALESCE(allocation.occupied_qty,0)<>stock.occupied_qty
            OR COALESCE(allocation.allocated_qty,0)-COALESCE(allocation.occupied_qty,0)<>stock.available_qty);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'allocation与ERP三分量不守恒';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM trk_stock_reservation_item item
      LEFT JOIN (
          SELECT reservation_item_id,SUM(remaining_qty) remaining_qty
            FROM wms_erp_stock_reservation_allocation
           WHERE tenant_id=1 AND deleted=b'0'
           GROUP BY reservation_item_id
      ) location_owner ON location_owner.reservation_item_id=item.id
      JOIN trk_stock stock ON stock.id=item.stock_id
     WHERE item.tenant_id=1 AND item.deleted=b'0' AND stock.warehouse_id=320118
       AND item.remaining_qty<>COALESCE(location_owner.remaining_qty,0);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'reservation item与库位owner不守恒';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM wms_erp_stock_allocation allocation
      LEFT JOIN (
          SELECT stock_allocation_id,SUM(remaining_qty) remaining_qty
            FROM wms_erp_stock_reservation_allocation
           WHERE tenant_id=1 AND deleted=b'0'
           GROUP BY stock_allocation_id
      ) owner ON owner.stock_allocation_id=allocation.id
      JOIN trk_stock stock ON stock.id=allocation.erp_stock_id
     WHERE allocation.tenant_id=1 AND stock.warehouse_id=320118 AND stock.deleted=b'0'
       AND allocation.occupied_qty<>COALESCE(owner.remaining_qty,0);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'allocation occupied与库位owner不守恒';
    END IF;

    SELECT COALESCE(SUM(available_qty),0),COALESCE(SUM(occupied_qty),0),COALESCE(SUM(total_qty),0)
      INTO v_available,v_occupied,v_total
      FROM trk_stock WHERE warehouse_id=320118 AND deleted=b'0';
    IF v_available<>31822 OR v_occupied<>4250 OR v_total<>36072 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '最终三分量必须为31822/4250/36072';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM trk_stock_reservation reservation
      JOIN trk_stock_reservation_item item
        ON item.tenant_id=reservation.tenant_id AND item.reservation_id=reservation.id
     WHERE reservation.tenant_id=1 AND reservation.status='ORPHANED'
       AND item.remaining_qty<>0;
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ORPHANED remaining必须为0';
    END IF;

    SELECT COUNT(*) INTO v_count FROM wms_packing_task_stock_selection
     WHERE tenant_id=1 AND id IN (5,6)
       AND (reservation_id IS NULL OR reservation_item_id IS NULL
            OR erp_stock_id IS NULL OR stock_allocation_id IS NULL);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'selection仍有无主库存引用';
    END IF;

    SELECT COUNT(*) INTO v_count FROM wms_dispatchpicklist
     WHERE id IN (1,2,3,4,5,6,7,8) AND is_update_stock=0
       AND (reservation_id IS NULL OR reservation_item_id IS NULL
            OR erp_stock_id IS NULL OR stock_allocation_id IS NULL);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'pending pick仍有无主库存引用';
    END IF;

    COMMIT;
    DROP TEMPORARY TABLE IF EXISTS tmp_wms_new_owner;
    DROP TEMPORARY TABLE IF EXISTS tmp_old_pick_owner;
    DROP TEMPORARY TABLE IF EXISTS tmp_wms_erp_owner_location;
    DROP TEMPORARY TABLE IF EXISTS tmp_wms_legacy_stock_map;
END$$

DELIMITER ;

-- 安全默认：创建过程后不执行。维护窗口核对无误后，人工取消下一行注释。
-- CALL `wms_cutover_reservation_320118_20260821`();

-- 成功后可删除过程；若需要读取结果进行人工验收，可暂时保留到窗口结束。
-- DROP PROCEDURE IF EXISTS `wms_cutover_reservation_320118_20260821`;

-- 本脚本不自动更新 runtime mode。双方部署完成并确认ERP门禁开启后，另行同步切换：
-- UPDATE wms_inventory_runtime_config
--    SET mode='CANONICAL_ERP', maintenance_enabled=0, cutover_time=NOW(6),
--        row_version=row_version+1, updater='reservation-cutover', update_time=NOW(6)
--  WHERE tenant_id=1 AND erp_warehouse_id=320118
--    AND mode='LEGACY_READ' AND maintenance_enabled=1;
