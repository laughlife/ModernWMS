-- 手工维护窗口脚本：把旧 wms_stock 转为 ERP 库存的位置分配。
-- 本文件不属于 Flyway 自动迁移目录，禁止在未完成备份、旧锁清零和双系统同步切换前执行。
-- 使用前必须显式设置目标租户、ERP仓库，并先把 runtime_config 置为 LEGACY_READ + maintenance_enabled=1。
SET @cutover_tenant_id = NULL;
SET @cutover_erp_warehouse_id = NULL;

DROP PROCEDURE IF EXISTS `wms_prepare_erp_stock_allocation_cutover`;
DELIMITER $$
CREATE PROCEDURE `wms_prepare_erp_stock_allocation_cutover`(
    IN p_tenant_id bigint,
    IN p_erp_warehouse_id bigint
)
BEGIN
    DECLARE v_count bigint DEFAULT 0;
    DECLARE v_runtime_config_id bigint DEFAULT NULL;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        DROP TEMPORARY TABLE IF EXISTS `tmp_wms_legacy_stock_map`;
        RESIGNAL;
    END;

    IF p_tenant_id IS NULL OR p_tenant_id <= 0
       OR p_erp_warehouse_id IS NULL OR p_erp_warehouse_id <= 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '必须显式指定目标租户和ERP仓库';
    END IF;

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    START TRANSACTION;

    SELECT `id` INTO v_runtime_config_id
      FROM `wms_inventory_runtime_config`
     WHERE `tenant_id` = p_tenant_id
       AND `erp_warehouse_id` = p_erp_warehouse_id
       AND `mode` = 'LEGACY_READ'
       AND `maintenance_enabled` = 1
     FOR UPDATE;
    IF v_runtime_config_id IS NULL THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '仓库未处于LEGACY_READ库存维护窗口';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM `wms_erp_stock_allocation` allocation
      JOIN `trk_stock` stock ON stock.`id` = allocation.`erp_stock_id`
     WHERE allocation.`tenant_id` = p_tenant_id
       AND stock.`warehouse_id` = p_erp_warehouse_id
       AND stock.`deleted` = b'0';
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '目标仓已存在allocation，拒绝重复回填';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM `wms_packing_task_stock_selection` selection
      JOIN `wms_stock` legacy_stock ON legacy_stock.`id` = selection.`stock_id`
      JOIN `wms_goodslocation` location ON location.`id` = legacy_stock.`goods_location_id`
      JOIN `wms_warehouse` warehouse ON warehouse.`id` = location.`warehouse_id`
     WHERE selection.`tenant_id` = p_tenant_id
       AND warehouse.`erp_warehouse_id` = p_erp_warehouse_id;
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '仍有旧WMS装箱锁定，必须先完成或撤销';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM `wms_dispatchpicklist` pick
      JOIN `wms_stock` legacy_stock ON legacy_stock.`id` = pick.`stock_id`
      JOIN `wms_goodslocation` location ON location.`id` = legacy_stock.`goods_location_id`
      JOIN `wms_warehouse` warehouse ON warehouse.`id` = location.`warehouse_id`
     WHERE warehouse.`tenant_id` = p_tenant_id
       AND warehouse.`erp_warehouse_id` = p_erp_warehouse_id
       AND pick.`is_update_stock` = 0;
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '仍有旧WMS出库锁定，必须先完成或撤销';
    END IF;

    SELECT
      (SELECT COUNT(*)
         FROM `wms_stock` legacy_stock
         JOIN `wms_goodslocation` location ON location.`id`=legacy_stock.`goods_location_id`
         JOIN `wms_warehouse` warehouse ON warehouse.`id`=location.`warehouse_id`
        WHERE legacy_stock.`tenant_id`=p_tenant_id
          AND warehouse.`erp_warehouse_id`=p_erp_warehouse_id
          AND legacy_stock.`is_freeze`=1)
      + (SELECT COUNT(*)
           FROM `wms_stockmove` move_job
           JOIN `wms_goodslocation` location ON location.`id`=move_job.`orig_goods_location_id`
           JOIN `wms_warehouse` warehouse ON warehouse.`id`=location.`warehouse_id`
          WHERE move_job.`tenant_id`=p_tenant_id
            AND warehouse.`erp_warehouse_id`=p_erp_warehouse_id
            AND move_job.`move_status`=0)
      + (SELECT COUNT(*)
           FROM `wms_stockprocessdetail` process_detail
           JOIN `wms_goodslocation` location ON location.`id`=process_detail.`goods_location_id`
           JOIN `wms_warehouse` warehouse ON warehouse.`id`=location.`warehouse_id`
          WHERE process_detail.`tenant_id`=p_tenant_id
            AND warehouse.`erp_warehouse_id`=p_erp_warehouse_id
            AND process_detail.`is_update_stock`=0)
      + (SELECT COUNT(*)
           FROM `wms_stocktaking` taking
           JOIN `wms_goodslocation` location ON location.`id`=taking.`goods_location_id`
           JOIN `wms_warehouse` warehouse ON warehouse.`id`=location.`warehouse_id`
          WHERE taking.`tenant_id`=p_tenant_id
            AND warehouse.`erp_warehouse_id`=p_erp_warehouse_id
            AND taking.`job_status`=0)
      + (SELECT COUNT(*)
           FROM `wms_stockadjust` adjustment
           JOIN `wms_goodslocation` location ON location.`id`=adjustment.`goods_location_id`
           JOIN `wms_warehouse` warehouse ON warehouse.`id`=location.`warehouse_id`
          WHERE adjustment.`tenant_id`=p_tenant_id
            AND warehouse.`erp_warehouse_id`=p_erp_warehouse_id
            AND adjustment.`is_update_stock`=0)
      INTO v_count;
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '仍有旧冻结、移库、加工、盘点或调整单未清零';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM `wms_stock` legacy_stock
      LEFT JOIN `wms_goodslocation` location
        ON location.`id`=legacy_stock.`goods_location_id`
      LEFT JOIN `wms_warehouse` warehouse ON warehouse.`id`=location.`warehouse_id`
      LEFT JOIN `wms_warehousearea` area ON area.`id`=location.`warehouse_area_id`
     WHERE legacy_stock.`tenant_id`=p_tenant_id
       AND legacy_stock.`qty`>0
       AND (location.`id` IS NULL OR location.`tenant_id`<>p_tenant_id OR location.`is_valid`=0
            OR warehouse.`id` IS NULL OR warehouse.`tenant_id`<>p_tenant_id OR warehouse.`is_valid`=0
            OR area.`id` IS NULL OR area.`tenant_id`<>p_tenant_id OR area.`is_valid`=0
            OR area.`warehouse_id`<>warehouse.`id`);
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '旧库存存在跨租户、停用或仓区关系无效的库位';
    END IF;

    DROP TEMPORARY TABLE IF EXISTS `tmp_wms_legacy_stock_map`;
    CREATE TEMPORARY TABLE `tmp_wms_legacy_stock_map` AS
    SELECT legacy_stock.`id` AS legacy_stock_id,
           MIN(receipt_item.`erp_stock_id`) AS erp_stock_id,
           COUNT(DISTINCT receipt_item.`erp_stock_id`) AS erp_stock_count,
           legacy_stock.`tenant_id`, legacy_stock.`sku_id`, legacy_stock.`goods_location_id`,
           location.`warehouse_area_id`, legacy_stock.`goods_owner_id`,
           legacy_stock.`series_number`, legacy_stock.`expiry_date`,
           legacy_stock.`price`, DATE(legacy_stock.`putaway_date`) AS putaway_date,
           legacy_stock.`qty`
      FROM `wms_stock` legacy_stock
      JOIN `wms_goodslocation` location ON location.`id` = legacy_stock.`goods_location_id`
      JOIN `wms_warehouse` warehouse ON warehouse.`id` = location.`warehouse_id`
      LEFT JOIN `wms_erp_receipt_item` receipt_item
        ON receipt_item.`tenant_id` = legacy_stock.`tenant_id`
       AND receipt_item.`wms_stock_id` = legacy_stock.`id`
       AND receipt_item.`erp_stock_id` IS NOT NULL
     WHERE legacy_stock.`tenant_id` = p_tenant_id
       AND legacy_stock.`qty` > 0
       AND warehouse.`erp_warehouse_id` = p_erp_warehouse_id
     GROUP BY legacy_stock.`id`, legacy_stock.`tenant_id`, legacy_stock.`sku_id`, legacy_stock.`goods_location_id`,
              location.`warehouse_area_id`, legacy_stock.`goods_owner_id`,
              legacy_stock.`series_number`, legacy_stock.`expiry_date`, legacy_stock.`price`,
              DATE(legacy_stock.`putaway_date`), legacy_stock.`qty`;

    SELECT COUNT(*) INTO v_count
      FROM `tmp_wms_legacy_stock_map`
     WHERE `erp_stock_count` <> 1 OR `erp_stock_id` IS NULL
        OR `qty` <= 0 OR CHAR_LENGTH(`series_number`) > 128;
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '旧WMS库存无法唯一映射ERP库存、数量无效或批次号超过128字符';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM `tmp_wms_legacy_stock_map` map
      LEFT JOIN `trk_stock` stock ON stock.`id` = map.`erp_stock_id` AND stock.`deleted` = b'0'
      LEFT JOIN `wms_erp_goods_owner_map` owner_map
        ON owner_map.`tenant_id` = map.`tenant_id`
       AND owner_map.`wms_goods_owner_id` = map.`goods_owner_id`
       AND owner_map.`erp_dept_id` <=> stock.`dept_id`
       AND owner_map.`erp_order_user_id` <=> stock.`order_user_id`
      LEFT JOIN `wms_erp_commodity_map` commodity_map
        ON commodity_map.`tenant_id` = map.`tenant_id`
       AND commodity_map.`erp_commodity_id` = stock.`commodity_id`
     WHERE stock.`id` IS NULL
        OR stock.`warehouse_id` <> p_erp_warehouse_id
        OR COALESCE(stock.`stock_batch_no`,'') <> 'POOL'
        OR owner_map.`id` IS NULL
        OR commodity_map.`id` IS NULL
        OR commodity_map.`wms_sku_id` <> map.`sku_id`;
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '旧库位所属仓或货主映射与ERP库存不一致';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM `trk_stock` stock
      JOIN (
        SELECT `erp_stock_id`, SUM(`qty`) AS legacy_qty
          FROM `tmp_wms_legacy_stock_map`
         GROUP BY `erp_stock_id`
      ) legacy ON legacy.`erp_stock_id` = stock.`id`
     WHERE stock.`deleted` = b'0'
       AND (stock.`total_qty` < legacy.`legacy_qty`
            OR stock.`available_qty` < 0 OR stock.`occupied_qty` <> 0
            OR stock.`total_qty` <> stock.`available_qty` + stock.`occupied_qty`);
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ERP库存守恒异常、旧分配溢出或旧锁尚未清零';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM `trk_stock` stock
     WHERE stock.`warehouse_id` = p_erp_warehouse_id
       AND stock.`deleted` = b'0'
       AND (COALESCE(stock.`stock_batch_no`,'') <> 'POOL'
            OR stock.`available_qty` < 0 OR stock.`occupied_qty` <> 0 OR stock.`total_qty` < 0
            OR stock.`total_qty` <> stock.`available_qty` + stock.`occupied_qty`);
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '目标仓仍有非POOL、负数、占用或三分量异常库存';
    END IF;

    INSERT INTO `wms_erp_stock_allocation`
        (`tenant_id`,`erp_stock_id`,`warehouse_area_id`,`goods_location_id`,`goods_owner_id`,
         `series_number`,`expiry_date`,`price`,`putaway_date`,`allocated_qty`,`occupied_qty`,
         `location_state`,`row_version`,`creator`,`create_time`,`updater`,`update_time`)
    SELECT map.`tenant_id`, map.`erp_stock_id`, map.`warehouse_area_id`, map.`goods_location_id`,
           map.`goods_owner_id`, map.`series_number`, map.`expiry_date`, map.`price`,
           map.`putaway_date`, SUM(map.`qty`), 0, 'ACTIVE', 0,
           'manual-cutover', NOW(6), 'manual-cutover', NOW(6)
      FROM `tmp_wms_legacy_stock_map` map
     WHERE map.`qty` > 0
     GROUP BY map.`tenant_id`, map.`erp_stock_id`, map.`warehouse_area_id`,
              map.`goods_location_id`, map.`goods_owner_id`, map.`series_number`,
              map.`expiry_date`, map.`price`, map.`putaway_date`
    HAVING SUM(map.`qty`) > 0;

    SELECT COUNT(*) INTO v_count
      FROM (
        SELECT stock.`id`
          FROM `trk_stock` stock
          LEFT JOIN `wms_erp_goods_owner_map` owner_map
            ON owner_map.`tenant_id` = p_tenant_id
           AND owner_map.`erp_dept_id` <=> stock.`dept_id`
           AND owner_map.`erp_order_user_id` <=> stock.`order_user_id`
         WHERE stock.`warehouse_id` = p_erp_warehouse_id
           AND stock.`deleted` = b'0'
           AND stock.`total_qty` > 0
         GROUP BY stock.`id`
        HAVING COUNT(owner_map.`id`) <> 1
      ) invalid_owner;
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ERP库存无法唯一映射WMS货主';
    END IF;

    INSERT INTO `wms_erp_stock_allocation`
        (`tenant_id`,`erp_stock_id`,`warehouse_area_id`,`goods_location_id`,`goods_owner_id`,
         `series_number`,`expiry_date`,`price`,`putaway_date`,`allocated_qty`,`occupied_qty`,
         `location_state`,`row_version`,`creator`,`create_time`,`updater`,`update_time`)
    SELECT p_tenant_id, stock.`id`, NULL, NULL, owner_map.`wms_goods_owner_id`,
           '', '9999-12-31 00:00:00.000000', 0, CURRENT_DATE,
           stock.`total_qty` - COALESCE(allocated.`allocated_qty`,0), 0, 'UNLOCATED', 0,
           'manual-cutover', NOW(6), 'manual-cutover', NOW(6)
      FROM `trk_stock` stock
      JOIN `wms_erp_goods_owner_map` owner_map
        ON owner_map.`tenant_id` = p_tenant_id
       AND owner_map.`erp_dept_id` <=> stock.`dept_id`
       AND owner_map.`erp_order_user_id` <=> stock.`order_user_id`
      LEFT JOIN (
        SELECT `erp_stock_id`, SUM(`allocated_qty`) AS allocated_qty
          FROM `wms_erp_stock_allocation`
         WHERE `tenant_id` = p_tenant_id
         GROUP BY `erp_stock_id`
      ) allocated ON allocated.`erp_stock_id` = stock.`id`
     WHERE stock.`warehouse_id` = p_erp_warehouse_id
       AND stock.`deleted` = b'0'
       AND stock.`total_qty` > COALESCE(allocated.`allocated_qty`,0);

    INSERT INTO `wms_erp_stock_allocation_log`
        (`tenant_id`,`operation_key`,`biz_type`,`biz_id`,`biz_item_id`,`event_type`,
         `erp_stock_id`,`allocation_id`,`counterpart_allocation_id`,`erp_stock_record_id`,
         `allocated_delta`,`occupied_delta`,`before_allocated_qty`,`after_allocated_qty`,
         `before_occupied_qty`,`after_occupied_qty`,`operator`,`operate_time`,`remark`)
    SELECT allocation.`tenant_id`, CONCAT('MWMS:CUTOVER:', allocation.`id`),
           'INVENTORY_CUTOVER', p_erp_warehouse_id, allocation.`id`, 'BACKFILL',
           allocation.`erp_stock_id`, allocation.`id`, NULL, NULL,
           allocation.`allocated_qty`, allocation.`occupied_qty`, 0, allocation.`allocated_qty`,
           0, allocation.`occupied_qty`, 'manual-cutover', NOW(6),
           '旧WMS库存仅迁移库位与货主，不增加ERP库存'
      FROM `wms_erp_stock_allocation` allocation
      JOIN `trk_stock` stock ON stock.`id` = allocation.`erp_stock_id`
     WHERE allocation.`tenant_id` = p_tenant_id
       AND stock.`warehouse_id` = p_erp_warehouse_id
       AND stock.`deleted` = b'0';

    SELECT COUNT(*) INTO v_count
      FROM `trk_stock` stock
      LEFT JOIN (
        SELECT `erp_stock_id`, SUM(`allocated_qty`) AS allocated_qty,
               SUM(`occupied_qty`) AS occupied_qty
          FROM `wms_erp_stock_allocation`
         WHERE `tenant_id` = p_tenant_id
           AND `location_state` IN ('ACTIVE','UNLOCATED')
         GROUP BY `erp_stock_id`
      ) allocation ON allocation.`erp_stock_id` = stock.`id`
     WHERE stock.`warehouse_id` = p_erp_warehouse_id
       AND stock.`deleted` = b'0'
       AND (COALESCE(allocation.`allocated_qty`,0) <> stock.`total_qty`
            OR COALESCE(allocation.`occupied_qty`,0) <> stock.`occupied_qty`
            OR COALESCE(allocation.`allocated_qty`,0) - COALESCE(allocation.`occupied_qty`,0)
               <> stock.`available_qty`);
    IF v_count <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'allocation与ERP库存数量守恒校验失败';
    END IF;

    COMMIT;
    DROP TEMPORARY TABLE IF EXISTS `tmp_wms_legacy_stock_map`;
END$$
DELIMITER ;

-- 仅在人工确认变量、备份和维护门禁后取消下一行注释执行：
-- CALL `wms_prepare_erp_stock_allocation_cutover`(@cutover_tenant_id, @cutover_erp_warehouse_id);

DROP PROCEDURE IF EXISTS `wms_prepare_erp_stock_allocation_cutover`;

-- 回填完成后仍保持 LEGACY_READ + maintenance_enabled=1。
-- 必须与 ERP_STOCK_WMS_MANAGED_WAREHOUSE_GUARD_ENABLED=true 在同一窗口同步切换；禁止单边提前 UPDATE 为 CANONICAL_ERP。
