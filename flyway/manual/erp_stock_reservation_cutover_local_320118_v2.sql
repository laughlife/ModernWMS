-- 深圳仓 ERP 唯一库存接续 V2。
-- 精确适用范围：tenant_id=1 / ERP warehouse_id=320118 / 2026-08-21 生产快照恢复副本。
-- 前置：Ruoyi V2 历史 reservation 回填已完成，ERP 三分量为 58888/2250/61138。
-- 结果：7 条 ModernWMS 永久来源真实 RESERVE 2050，最终为 56838/4300/61138；
--       96 条历史收货引用迁移完成后，仅删除深圳仓 92 条 wms_stock。
-- 安全：默认不 CALL；过程内 SERIALIZABLE + EXIT HANDLER，任一门禁失败整体回滚。
-- 重放：成功后 runtime=CANONICAL_ERP；完整重放只做最终态验收，不再写数据。

SET NAMES utf8mb4;

DELIMITER $$

DROP PROCEDURE IF EXISTS `wms_cutover_reservation_320118_20260821_v2`$$
CREATE PROCEDURE `wms_cutover_reservation_320118_20260821_v2`()
main: BEGIN
    DECLARE v_count BIGINT DEFAULT 0;
    DECLARE v_qty BIGINT DEFAULT 0;
    DECLARE v_aux BIGINT DEFAULT 0;
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
        DROP TEMPORARY TABLE IF EXISTS tmp_v2_new_owner;
        DROP TEMPORARY TABLE IF EXISTS tmp_v2_old_pick_owner;
        DROP TEMPORARY TABLE IF EXISTS tmp_v2_erp_owner;
        DROP TEMPORARY TABLE IF EXISTS tmp_v2_allocation_source;
        DROP TEMPORARY TABLE IF EXISTS tmp_v2_missing_area;
        DROP TEMPORARY TABLE IF EXISTS tmp_v2_missing_area_candidate;
        DROP TEMPORARY TABLE IF EXISTS tmp_v2_legacy_map;
        RESIGNAL;
    END;

    DROP TEMPORARY TABLE IF EXISTS tmp_v2_new_owner;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_old_pick_owner;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_erp_owner;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_allocation_source;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_missing_area;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_missing_area_candidate;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_legacy_map;

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    START TRANSACTION;

    -- 完整重放只接受严格最终态；任何不同请求或残缺结果均拒绝。
    SELECT COUNT(*) INTO v_count
      FROM wms_inventory_runtime_config
     WHERE tenant_id=1 AND erp_warehouse_id=320118
       AND mode='CANONICAL_ERP' AND maintenance_enabled=0
     FOR UPDATE;
    IF v_count=1 THEN
        SELECT COUNT(*),COALESCE(SUM(available_qty),0),COALESCE(SUM(occupied_qty),0),COALESCE(SUM(total_qty),0)
          INTO v_count,v_available,v_occupied,v_total
          FROM trk_stock WHERE warehouse_id=320118 AND deleted=b'0';
        IF v_count<>95 OR v_available<>56838 OR v_occupied<>4300 OR v_total<>61138 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='V2重放拒绝：ERP最终三分量不匹配';
        END IF;

        SELECT COUNT(*),COALESCE(SUM(a.allocated_qty),0),COALESCE(SUM(a.occupied_qty),0)
          INTO v_count,v_qty,v_aux
          FROM wms_erp_stock_allocation a
          JOIN trk_stock s ON s.id=a.erp_stock_id AND s.deleted=b'0'
         WHERE a.tenant_id=1 AND s.warehouse_id=320118
           AND a.location_state='UNLOCATED' AND a.goods_location_id IS NULL;
        IF v_count<>95 OR v_qty<>61138 OR v_aux<>4300 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='V2重放拒绝：allocation最终态不匹配';
        END IF;

        SELECT COUNT(*),COALESCE(SUM(item.remaining_qty),0)
          INTO v_count,v_qty
          FROM trk_stock_reservation_item item
          JOIN trk_stock s ON s.id=item.stock_id AND s.deleted=b'0'
         WHERE item.tenant_id=1 AND item.deleted=b'0' AND s.warehouse_id=320118
           AND item.remaining_qty>0;
        IF v_count<>14 OR v_qty<>4300 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='V2重放拒绝：reservation最终态不匹配';
        END IF;

        SELECT COUNT(*) INTO v_count
          FROM trk_stock_reservation_command
         WHERE tenant_id=1 AND namespace='WMS_RESERVATION_MIGRATION_V2'
           AND result_status='SUCCEEDED' AND deleted=b'0';
        IF v_count<>7 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='V2重放拒绝：迁移命令不是7条成功记录';
        END IF;

        SELECT COUNT(*) INTO v_count
          FROM wms_stock legacy
          JOIN wms_goodslocation location ON location.id=legacy.goods_location_id
          JOIN wms_warehouse warehouse ON warehouse.id=location.warehouse_id
         WHERE legacy.tenant_id=1 AND warehouse.tenant_id=1
           AND warehouse.erp_warehouse_id=320118;
        IF v_count<>0 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='V2重放拒绝：目标旧库存仍存在';
        END IF;

        SELECT COUNT(*) INTO v_count
          FROM wms_erp_receipt_item item
          JOIN wms_erp_stock_allocation a ON a.id=item.primary_stock_allocation_id
          JOIN trk_stock s ON s.id=a.erp_stock_id AND s.warehouse_id=320118 AND s.deleted=b'0'
         WHERE item.tenant_id=1 AND item.wms_stock_id IS NULL;
        IF v_count<>96 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='V2重放拒绝：历史收货引用不是96条';
        END IF;
        COMMIT;
        LEAVE main;
    END IF;

    -- 首次执行时明确建立并锁定 LEGACY_READ 维护窗口；不依赖缺行默认值。
    SELECT COUNT(*) INTO v_count
      FROM wms_inventory_runtime_config
     WHERE tenant_id=1 AND erp_warehouse_id=320118
     FOR UPDATE;
    IF v_count=0 THEN
        INSERT INTO wms_inventory_runtime_config
            (tenant_id,erp_warehouse_id,mode,maintenance_enabled,cutover_time,row_version,
             creator,create_time,updater,update_time)
        VALUES (1,320118,'LEGACY_READ',1,NULL,0,
                'reservation-cutover-v2',NOW(6),'reservation-cutover-v2',NOW(6));
    ELSE
        UPDATE wms_inventory_runtime_config
           SET maintenance_enabled=1,row_version=row_version+1,
               updater='reservation-cutover-v2',update_time=NOW(6)
         WHERE tenant_id=1 AND erp_warehouse_id=320118 AND mode='LEGACY_READ';
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='runtime不是可进入维护的LEGACY_READ';
        END IF;
    END IF;

    -- ERP V2 精确入口。
    SELECT COUNT(*),COALESCE(SUM(available_qty),0),COALESCE(SUM(occupied_qty),0),COALESCE(SUM(total_qty),0)
      INTO v_count,v_available,v_occupied,v_total
      FROM trk_stock
     WHERE warehouse_id=320118 AND deleted=b'0'
     FOR UPDATE;
    IF v_count<>95 OR v_available<>58888 OR v_occupied<>2250 OR v_total<>61138 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='必须先完成ERP V2并达到95/58888/2250/61138';
    END IF;

    SELECT COUNT(*),
           SUM(item.status='ACTIVE'),SUM(item.status='CONSUMED'),
           COALESCE(SUM(item.remaining_qty),0),COALESCE(SUM(item.consumed_qty),0)
      INTO v_count,v_qty,v_aux,v_occupied,v_total
      FROM trk_stock_reservation_item item
      JOIN trk_stock s ON s.id=item.stock_id AND s.deleted=b'0'
     WHERE item.tenant_id=1 AND item.deleted=b'0' AND s.warehouse_id=320118
     FOR UPDATE;
    IF v_count<>10 OR v_qty<>7 OR v_aux<>3 OR v_occupied<>2250 OR v_total<>800 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ERP reservation必须为10 item（7 ACTIVE/3 CONSUMED）';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM trk_stock s
      LEFT JOIN (
          SELECT item.stock_id,SUM(item.remaining_qty) remaining_qty
            FROM trk_stock_reservation_item item
           WHERE item.tenant_id=1 AND item.deleted=b'0'
           GROUP BY item.stock_id
      ) owner ON owner.stock_id=s.id
     WHERE s.warehouse_id=320118 AND s.deleted=b'0'
       AND s.occupied_qty<>COALESCE(owner.remaining_qty,0);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ERP V2后occupied与reservation remaining不守恒';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM wms_erp_stock_allocation a
      JOIN trk_stock s ON s.id=a.erp_stock_id
     WHERE a.tenant_id=1 AND s.warehouse_id=320118 AND s.deleted=b'0';
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='目标仓已存在allocation，拒绝首次执行';
    END IF;
    SELECT COUNT(*) INTO v_count FROM wms_erp_stock_reservation_allocation WHERE tenant_id=1;
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='首次执行要求WMS allocation reservation为空';
    END IF;
    SELECT COUNT(*) INTO v_count
      FROM trk_stock_reservation_command
     WHERE tenant_id=1 AND namespace='WMS_RESERVATION_MIGRATION_V2' AND deleted=b'0';
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='存在V2 namespace但runtime未完成，拒绝不同请求';
    END IF;

    -- 未完成旧作业必须精确为0；仅已归因的pick/selection允许接续。
    SELECT
      (SELECT COUNT(*) FROM wms_stockfreeze f WHERE f.tenant_id=1)
      +(SELECT COUNT(*) FROM wms_stockmove m
          JOIN wms_goodslocation l ON l.id=m.orig_goods_location_id
          JOIN wms_warehouse w ON w.id=l.warehouse_id
         WHERE m.tenant_id=1 AND w.erp_warehouse_id=320118 AND m.move_status=0)
      +(SELECT COUNT(*) FROM wms_stockadjust a
          JOIN wms_goodslocation l ON l.id=a.goods_location_id
          JOIN wms_warehouse w ON w.id=l.warehouse_id
         WHERE a.tenant_id=1 AND w.erp_warehouse_id=320118 AND a.is_update_stock=0)
      +(SELECT COUNT(*) FROM wms_stocktaking t
          JOIN wms_goodslocation l ON l.id=t.goods_location_id
          JOIN wms_warehouse w ON w.id=l.warehouse_id
         WHERE t.tenant_id=1 AND w.erp_warehouse_id=320118 AND t.job_status=0)
      +(SELECT COUNT(*) FROM wms_stockprocessdetail d
          JOIN wms_goodslocation l ON l.id=d.goods_location_id
          JOIN wms_warehouse w ON w.id=l.warehouse_id
         WHERE d.tenant_id=1 AND w.erp_warehouse_id=320118 AND d.is_update_stock=0)
      INTO v_count;
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='仍有冻结/移库/调整/盘点/加工未完成';
    END IF;

    -- 92 条旧库存通过96条历史收货精确映射到92个ERP stock。
    CREATE TEMPORARY TABLE tmp_v2_legacy_map AS
    SELECT legacy.id legacy_stock_id,MIN(item.erp_stock_id) erp_stock_id,
           COUNT(DISTINCT item.erp_stock_id) erp_stock_count,
           legacy.sku_id,legacy.goods_owner_id,
           location.warehouse_area_id,legacy.goods_location_id legacy_goods_location_id,
           legacy.series_number,legacy.expiry_date,legacy.price,DATE(legacy.putaway_date) putaway_date,
           legacy.qty legacy_qty
      FROM wms_stock legacy
      JOIN wms_goodslocation location ON location.id=legacy.goods_location_id
      JOIN wms_warehouse warehouse ON warehouse.id=location.warehouse_id
      LEFT JOIN wms_erp_receipt_item item
        ON item.tenant_id=legacy.tenant_id AND item.wms_stock_id=legacy.id
       AND item.erp_stock_id IS NOT NULL
     WHERE legacy.tenant_id=1 AND warehouse.tenant_id=1
       AND warehouse.erp_warehouse_id=320118
     GROUP BY legacy.id,legacy.sku_id,legacy.goods_owner_id,location.warehouse_area_id,
              legacy.goods_location_id,legacy.series_number,legacy.expiry_date,
              legacy.price,DATE(legacy.putaway_date),legacy.qty;

    SELECT COUNT(*),COALESCE(SUM(legacy_qty),0),COUNT(DISTINCT erp_stock_id)
      INTO v_count,v_qty,v_aux FROM tmp_v2_legacy_map;
    IF v_count<>92 OR v_qty<>61638 OR v_aux<>92 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='旧WMS基线必须为92行/61638/92stock';
    END IF;

    SELECT COUNT(*),COUNT(DISTINCT map.legacy_goods_location_id)
      INTO v_count,v_qty
      FROM tmp_v2_legacy_map map
      JOIN wms_goodslocation location ON location.id=map.legacy_goods_location_id
     WHERE location.tag_number LIKE 'AREA-AUTO-%';
    IF v_count<>92 OR v_qty<>9 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='旧库存必须全部来自9个AREA-AUTO假库位';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM tmp_v2_legacy_map map
      LEFT JOIN trk_stock stock ON stock.id=map.erp_stock_id AND stock.deleted=b'0'
      LEFT JOIN wms_warehousearea area ON area.id=map.warehouse_area_id
      LEFT JOIN wms_erp_goods_owner_map owner_map
        ON owner_map.tenant_id=1 AND owner_map.wms_goods_owner_id=map.goods_owner_id
       AND owner_map.erp_dept_id<=>stock.dept_id
       AND owner_map.erp_order_user_id<=>stock.order_user_id
      LEFT JOIN wms_erp_commodity_map commodity_map
        ON commodity_map.tenant_id=1 AND commodity_map.erp_commodity_id=stock.commodity_id
     WHERE map.erp_stock_count<>1 OR stock.id IS NULL OR stock.warehouse_id<>320118
        OR COALESCE(stock.stock_batch_no,'')<>'POOL'
        OR area.id IS NULL OR area.tenant_id<>1 OR area.is_valid=0
        OR owner_map.id IS NULL OR commodity_map.id IS NULL OR commodity_map.wms_sku_id<>map.sku_id;
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='旧库存ERP/真实库区/货主/SKU映射不唯一';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM tmp_v2_legacy_map map
      JOIN trk_stock stock ON stock.id=map.erp_stock_id
     WHERE (map.erp_stock_id=1412 AND map.legacy_qty=386 AND stock.total_qty=786)
        OR (map.erp_stock_id=1437 AND map.legacy_qty=0 AND stock.total_qty=300)
        OR (map.erp_stock_id=1504 AND map.legacy_qty=900 AND stock.total_qty=500)
        OR (map.erp_stock_id=1516 AND map.legacy_qty=250 AND stock.total_qty=500);
    IF v_count<>4 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='四条已知旧WMS/ERP差异事实已变化';
    END IF;
    SELECT COUNT(*) INTO v_count
      FROM tmp_v2_legacy_map map
      JOIN trk_stock stock ON stock.id=map.erp_stock_id
     WHERE map.legacy_qty<>stock.total_qty;
    IF v_count<>4 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='旧WMS/ERP数量差异必须精确为4条';
    END IF;

    -- 无旧WMS的3条库存按部门链唯一真实库区落为UNLOCATED。
    CREATE TEMPORARY TABLE tmp_v2_missing_area_candidate AS
    WITH RECURSIVE dept_chain AS (
        SELECT stock.id stock_id,dept.id,dept.parent_id,0 depth
          FROM trk_stock stock
          JOIN system_dept dept ON dept.id=stock.dept_id AND dept.deleted=b'0' AND dept.status=0
          LEFT JOIN tmp_v2_legacy_map legacy ON legacy.erp_stock_id=stock.id
         WHERE stock.warehouse_id=320118 AND stock.deleted=b'0' AND legacy.erp_stock_id IS NULL
        UNION ALL
        SELECT child.stock_id,parent.id,parent.parent_id,child.depth+1
          FROM system_dept parent
          JOIN dept_chain child ON child.parent_id=parent.id
         WHERE parent.deleted=b'0' AND parent.status=0 AND child.depth<20
    )
    SELECT DISTINCT chain.stock_id,area.id warehouse_area_id
      FROM dept_chain chain
      JOIN wms_warehousearea_operator_group binding
        ON binding.tenant_id=1 AND binding.dept_id=chain.id
      JOIN wms_warehousearea area
        ON area.id=binding.warehouse_area_id AND area.tenant_id=1 AND area.is_valid=1
      JOIN wms_warehouse warehouse
        ON warehouse.id=area.warehouse_id AND warehouse.tenant_id=1
       AND warehouse.is_valid=1 AND warehouse.erp_warehouse_id=320118;

    SELECT COUNT(*) INTO v_count
      FROM (
          SELECT stock.id
            FROM trk_stock stock
            LEFT JOIN tmp_v2_legacy_map legacy ON legacy.erp_stock_id=stock.id
            LEFT JOIN tmp_v2_missing_area_candidate candidate ON candidate.stock_id=stock.id
           WHERE stock.warehouse_id=320118 AND stock.deleted=b'0' AND legacy.erp_stock_id IS NULL
           GROUP BY stock.id HAVING COUNT(DISTINCT candidate.warehouse_area_id)<>1
      ) invalid;
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='无旧WMS库存未找到唯一小组库区';
    END IF;

    CREATE TEMPORARY TABLE tmp_v2_missing_area AS
    SELECT stock_id,MIN(warehouse_area_id) warehouse_area_id
      FROM tmp_v2_missing_area_candidate GROUP BY stock_id;
    SELECT COUNT(*),SUM(stock_id IN (1456,1539,1547)) INTO v_count,v_qty FROM tmp_v2_missing_area;
    IF v_count<>3 OR v_qty<>3 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='无旧WMS库存必须精确为1456/1539/1547';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM trk_stock stock
     WHERE stock.warehouse_id=320118 AND stock.deleted=b'0'
       AND ((SELECT COUNT(*) FROM wms_erp_goods_owner_map owner_map
              WHERE owner_map.tenant_id=1
                AND owner_map.erp_dept_id<=>stock.dept_id
                AND owner_map.erp_order_user_id<=>stock.order_user_id)<>1
         OR (SELECT COUNT(*) FROM wms_erp_commodity_map commodity_map
              WHERE commodity_map.tenant_id=1
                AND commodity_map.erp_commodity_id=stock.commodity_id)<>1);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='95条ERP库存货主或SKU映射不唯一';
    END IF;

    -- allocation源一stock一行；数量只取ERP total，旧WMS仅提供真实area和批次快照。
    CREATE TEMPORARY TABLE tmp_v2_allocation_source AS
    SELECT stock.id erp_stock_id,map.warehouse_area_id,map.goods_owner_id,
           LEFT(map.series_number,128) series_number,map.expiry_date,map.price,map.putaway_date,
           stock.total_qty allocated_qty,stock.occupied_qty
      FROM tmp_v2_legacy_map map
      JOIN trk_stock stock ON stock.id=map.erp_stock_id
    UNION ALL
    SELECT stock.id,missing.warehouse_area_id,owner_map.wms_goods_owner_id,
           '',CAST('9999-12-31 00:00:00.000000' AS DATETIME(6)),0,CURRENT_DATE,
           stock.total_qty,stock.occupied_qty
      FROM tmp_v2_missing_area missing
      JOIN trk_stock stock ON stock.id=missing.stock_id
      JOIN wms_erp_goods_owner_map owner_map
        ON owner_map.tenant_id=1
       AND owner_map.erp_dept_id<=>stock.dept_id
       AND owner_map.erp_order_user_id<=>stock.order_user_id;

    SELECT COUNT(*),COUNT(DISTINCT erp_stock_id),COALESCE(SUM(allocated_qty),0),COALESCE(SUM(occupied_qty),0)
      INTO v_count,v_aux,v_qty,v_total FROM tmp_v2_allocation_source;
    IF v_count<>95 OR v_aux<>95 OR v_qty<>61138 OR v_total<>2250 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='allocation源必须为95/61138/2250';
    END IF;

    INSERT INTO wms_erp_stock_allocation
        (tenant_id,erp_stock_id,warehouse_area_id,goods_location_id,goods_owner_id,
         series_number,expiry_date,price,putaway_date,allocated_qty,occupied_qty,
         location_state,row_version,creator,create_time,updater,update_time)
    SELECT 1,erp_stock_id,warehouse_area_id,NULL,goods_owner_id,
           series_number,expiry_date,price,putaway_date,allocated_qty,occupied_qty,
           'UNLOCATED',0,'reservation-cutover-v2',NOW(6),'reservation-cutover-v2',NOW(6)
      FROM tmp_v2_allocation_source ORDER BY erp_stock_id;
    IF ROW_COUNT()<>95 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='allocation必须插入95行';
    END IF;

    INSERT INTO wms_erp_stock_allocation_log
        (tenant_id,operation_key,biz_type,biz_id,biz_item_id,event_type,
         erp_stock_id,allocation_id,counterpart_allocation_id,erp_stock_record_id,
         allocated_delta,occupied_delta,before_allocated_qty,after_allocated_qty,
         before_occupied_qty,after_occupied_qty,operator,operate_time,remark)
    SELECT 1,CONCAT('MWMS:CUTOVER:V2:',allocation.erp_stock_id),'INVENTORY_CUTOVER',320118,
           allocation.id,'BACKFILL',allocation.erp_stock_id,allocation.id,NULL,NULL,
           allocation.allocated_qty,allocation.occupied_qty,0,allocation.allocated_qty,
           0,allocation.occupied_qty,'reservation-cutover-v2',NOW(6),
           'ERP total为唯一真值；保留真实库区，清空AREA-AUTO假库位'
      FROM wms_erp_stock_allocation allocation
      JOIN trk_stock stock ON stock.id=allocation.erp_stock_id
     WHERE allocation.tenant_id=1 AND stock.warehouse_id=320118 AND stock.deleted=b'0';
    IF ROW_COUNT()<>95 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='allocation初始审计必须插入95行';
    END IF;

    -- 7个ERP ACTIVE item只建立位置owner，不重复RESERVE。
    CREATE TEMPORARY TABLE tmp_v2_erp_owner AS
    SELECT item.id reservation_item_id,item.reservation_id,item.stock_id,
           item.reserved_qty,item.released_qty,item.consumed_qty,item.remaining_qty,item.status,
           allocation.id allocation_id
      FROM trk_stock_reservation reservation
      JOIN trk_stock_reservation_item item
        ON item.tenant_id=reservation.tenant_id AND item.reservation_id=reservation.id
       AND item.deleted=b'0' AND item.status='ACTIVE' AND item.remaining_qty>0
      JOIN trk_stock stock ON stock.id=item.stock_id AND stock.warehouse_id=320118 AND stock.deleted=b'0'
      JOIN wms_erp_stock_allocation allocation
        ON allocation.tenant_id=1 AND allocation.erp_stock_id=item.stock_id
       AND allocation.location_state='UNLOCATED' AND allocation.goods_location_id IS NULL
     WHERE reservation.tenant_id=1 AND reservation.source_system='RUOYI'
       AND reservation.deleted=b'0';
    SELECT COUNT(*),COALESCE(SUM(remaining_qty),0) INTO v_count,v_qty FROM tmp_v2_erp_owner;
    IF v_count<>7 OR v_qty<>2250 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ERP ACTIVE owner必须为7行/2250';
    END IF;

    INSERT INTO wms_erp_stock_reservation_allocation
        (tenant_id,reservation_item_id,erp_stock_id,stock_allocation_id,
         reserved_qty,released_qty,consumed_qty,remaining_qty,status,row_version,
         creator,create_time,updater,update_time,deleted)
    SELECT 1,reservation_item_id,stock_id,allocation_id,
           reserved_qty,released_qty,consumed_qty,remaining_qty,status,0,
           'reservation-cutover-v2',NOW(6),'reservation-cutover-v2',NOW(6),b'0'
      FROM tmp_v2_erp_owner;
    IF ROW_COUNT()<>7 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ERP allocation owner必须插入7行';
    END IF;

    INSERT INTO wms_erp_stock_allocation_log
        (tenant_id,operation_key,reservation_id,reservation_item_id,
         biz_type,biz_id,biz_item_id,event_type,erp_stock_id,allocation_id,
         counterpart_allocation_id,erp_stock_record_id,allocated_delta,occupied_delta,
         before_allocated_qty,after_allocated_qty,before_occupied_qty,after_occupied_qty,
         operator,operate_time,remark)
    SELECT 1,CONCAT('MWMS:OWNER:V2:',owner.reservation_item_id),owner.reservation_id,
           owner.reservation_item_id,'RESERVATION_BACKFILL',320118,owner.reservation_item_id,
           'OWNER_ATTACH',owner.stock_id,owner.allocation_id,NULL,NULL,0,0,
           allocation.allocated_qty,allocation.allocated_qty,
           allocation.occupied_qty,allocation.occupied_qty,
           'reservation-cutover-v2',NOW(6),'RUOYI owner只关联allocation，不重复RESERVE'
      FROM tmp_v2_erp_owner owner
      JOIN wms_erp_stock_allocation allocation ON allocation.id=owner.allocation_id;
    IF ROW_COUNT()<>7 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ERP owner审计必须插入7行';
    END IF;

    -- pick1-4精确复用已有RUOYI owner（合计1050）。
    CREATE TEMPORARY TABLE tmp_v2_old_pick_owner AS
    SELECT pick.id pick_id,pick.pick_qty,item.reservation_id,item.id reservation_item_id,
           allocation.id allocation_id,map.erp_stock_id
      FROM wms_dispatchpicklist pick
      JOIN wms_dispatchlist dispatch ON dispatch.id=pick.dispatchlist_id
      JOIN trk_stock_move move_job ON move_job.no=dispatch.dispatch_no AND move_job.deleted=b'0'
      JOIN tmp_v2_legacy_map map ON map.legacy_stock_id=pick.stock_id
      JOIN trk_stock_reservation reservation
        ON reservation.tenant_id=1 AND reservation.source_system='RUOYI'
       AND reservation.carrier_biz_type='STOCK_MOVE' AND reservation.carrier_biz_id=move_job.id
       AND reservation.deleted=b'0'
      JOIN trk_stock_reservation_item item
        ON item.tenant_id=1 AND item.reservation_id=reservation.id
       AND item.stock_id=map.erp_stock_id AND item.remaining_qty=pick.pick_qty
       AND item.status='ACTIVE' AND item.deleted=b'0'
      JOIN wms_erp_stock_allocation allocation
        ON allocation.tenant_id=1 AND allocation.erp_stock_id=map.erp_stock_id
     WHERE pick.id IN (1,2,3,4) AND pick.is_update_stock=0
       AND pick.reservation_id IS NULL AND pick.reservation_item_id IS NULL;
    SELECT COUNT(*),COUNT(DISTINCT pick_id),COALESCE(SUM(pick_qty),0)
      INTO v_count,v_aux,v_qty FROM tmp_v2_old_pick_owner;
    IF v_count<>4 OR v_aux<>4 OR v_qty<>1050 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='pick1-4必须唯一复用4个RUOYI owner/1050';
    END IF;

    UPDATE wms_dispatchpicklist pick
    JOIN tmp_v2_old_pick_owner owner ON owner.pick_id=pick.id
       SET pick.erp_stock_id=owner.erp_stock_id,
           pick.stock_allocation_id=owner.allocation_id,
           pick.reservation_id=owner.reservation_id,
           pick.reservation_item_id=owner.reservation_item_id,
           pick.stock_id=0,pick.goods_location_id=NULL
     WHERE pick.id IN (1,2,3,4) AND pick.is_update_stock=0
       AND pick.reservation_id IS NULL AND pick.reservation_item_id IS NULL;
    IF ROW_COUNT()<>4 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='pick1-4引用迁移必须为4行';
    END IF;

    -- 7条独立WMS永久来源：pick5 + selection 1/2/3/4/6/7。
    CREATE TEMPORARY TABLE tmp_v2_new_owner (
        seq INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
        source_kind VARCHAR(32) NOT NULL,source_row_id BIGINT NOT NULL,
        task_id BIGINT NOT NULL,task_no VARCHAR(64) NOT NULL,source_item_id BIGINT NOT NULL,
        stock_id BIGINT NOT NULL,allocation_id BIGINT NOT NULL,action_qty BIGINT NOT NULL,
        source_line_key VARCHAR(128) NOT NULL,
        reservation_id BIGINT NULL,reservation_item_id BIGINT NULL,
        UNIQUE KEY uk_tmp_v2_source(source_kind,source_row_id),
        UNIQUE KEY uk_tmp_v2_line(task_id,source_line_key,stock_id)
    ) ENGINE=InnoDB;

    INSERT INTO tmp_v2_new_owner
        (source_kind,source_row_id,task_id,task_no,source_item_id,stock_id,
         allocation_id,action_qty,source_line_key)
    SELECT 'PACKING_SELECTION',selection.id,selection.sellfox_task_id,task.packing_task_sn,
           selection.sellfox_item_id,map.erp_stock_id,allocation.id,selection.qty,
           CONCAT('PACKING_SELECTION:',selection.id,':',selection.sellfox_item_id)
      FROM wms_packing_task_stock_selection selection
      JOIN ruiyi_sellfox_packing_task task ON task.sellfox_task_id=selection.sellfox_task_id
      JOIN tmp_v2_legacy_map map ON map.legacy_stock_id=selection.stock_id
      JOIN wms_erp_stock_allocation allocation
        ON allocation.tenant_id=1 AND allocation.erp_stock_id=map.erp_stock_id
     WHERE selection.tenant_id=1 AND selection.id IN (1,2,3,4,6,7)
       AND selection.reservation_id IS NULL AND selection.reservation_item_id IS NULL
     ORDER BY selection.id;

    INSERT INTO tmp_v2_new_owner
        (source_kind,source_row_id,task_id,task_no,source_item_id,stock_id,
         allocation_id,action_qty,source_line_key)
    SELECT 'PENDING_PICK',pick.id,task.source_task_id,task.source_task_no,
           task_item.source_item_id,map.erp_stock_id,allocation.id,pick.pick_qty,
           CONCAT('PENDING_PICK:',pick.id,':',task_item.source_item_id)
      FROM wms_dispatchpicklist pick
      JOIN wms_dispatch_packing_task_item task_item ON task_item.id=pick.packing_task_item_id
      JOIN wms_dispatch_packing_task task ON task.id=task_item.packing_task_id
      JOIN tmp_v2_legacy_map map ON map.legacy_stock_id=pick.stock_id
      JOIN wms_erp_stock_allocation allocation
        ON allocation.tenant_id=1 AND allocation.erp_stock_id=map.erp_stock_id
     WHERE pick.id=5 AND pick.is_update_stock=0
       AND pick.reservation_id IS NULL AND pick.reservation_item_id IS NULL;

    SELECT COUNT(*),COALESCE(SUM(action_qty),0) INTO v_count,v_qty FROM tmp_v2_new_owner;
    IF v_count<>7 OR v_qty<>2050 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='独立WMS来源必须为7行/2050';
    END IF;
    SELECT COUNT(*) INTO v_count FROM tmp_v2_new_owner
     WHERE NOT ((source_kind='PENDING_PICK' AND source_row_id=5 AND stock_id=1521 AND action_qty=250)
             OR (source_kind='PACKING_SELECTION' AND source_row_id=1 AND stock_id=1420 AND action_qty=300)
             OR (source_kind='PACKING_SELECTION' AND source_row_id=2 AND stock_id=1420 AND action_qty=200)
             OR (source_kind='PACKING_SELECTION' AND source_row_id=3 AND stock_id=1432 AND action_qty=500)
             OR (source_kind='PACKING_SELECTION' AND source_row_id=4 AND stock_id=1411 AND action_qty=300)
             OR (source_kind='PACKING_SELECTION' AND source_row_id=6 AND stock_id=1525 AND action_qty=250)
             OR (source_kind='PACKING_SELECTION' AND source_row_id=7 AND stock_id=1498 AND action_qty=250));
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='7条独立WMS来源事实已变化';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM trk_stock_reservation reservation
      JOIN (SELECT DISTINCT task_id FROM tmp_v2_new_owner) source ON source.task_id=reservation.biz_id
     WHERE reservation.tenant_id=1 AND reservation.source_system='MODERN_WMS'
       AND reservation.biz_type='PACKING_TASK' AND reservation.deleted=b'0';
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ModernWMS来源已存在reservation但runtime未完成';
    END IF;

    INSERT INTO trk_stock_reservation
        (reservation_no,source_system,biz_type,biz_id,biz_no,carrier_biz_type,carrier_biz_id,
         status,close_mode,source_snapshot_json,evidence_json,evidence_fingerprint,version,
         creator,create_time,updater,update_time,deleted,tenant_id)
    SELECT CONCAT('MWMS-V2-',LEFT(SHA2(CONCAT('1|MODERN_WMS|PACKING_TASK|',task_id),256),56)),
           'MODERN_WMS','PACKING_TASK',task_id,MIN(task_no),NULL,NULL,'ACTIVE',NULL,
           JSON_OBJECT('taskId',task_id,'taskNo',MIN(task_no)),
           JSON_OBJECT('migration','20260821_WMS_RESERVATION_CUTOVER_V2',
                       'rowCount',COUNT(*),'qty',SUM(action_qty)),
           SHA2(CAST(JSON_OBJECT('migration','20260821_WMS_RESERVATION_CUTOVER_V2',
                                 'rowCount',COUNT(*),'qty',SUM(action_qty)) AS CHAR),256),
           0,'reservation-cutover-v2',NOW(),'reservation-cutover-v2',NOW(),b'0',1
      FROM tmp_v2_new_owner GROUP BY task_id;
    SET v_aux=ROW_COUNT();
    SELECT COUNT(DISTINCT task_id) INTO v_count FROM tmp_v2_new_owner;
    IF v_aux<>v_count THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ModernWMS reservation master插入数错误';
    END IF;

    UPDATE tmp_v2_new_owner source
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
           JSON_OBJECT('sourceKind',source_kind,'sourceRowId',source_row_id,
                       'taskId',task_id,'sourceItemId',source_item_id,
                       'stockId',stock_id,'qty',action_qty),
           SHA2(CONCAT('MODERN_WMS|PACKING_TASK|',task_id,'|',source_line_key,'|',stock_id),256),
           0,0,0,0,0,'reservation-cutover-v2',NOW(),'reservation-cutover-v2',NOW(),b'0',1
      FROM tmp_v2_new_owner;
    IF ROW_COUNT()<>7 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ModernWMS reservation item必须插入7行';
    END IF;

    UPDATE tmp_v2_new_owner source
    JOIN trk_stock_reservation_item item
      ON item.tenant_id=1 AND item.reservation_id=source.reservation_id
     AND item.source_line_key=source.source_line_key AND item.stock_id=source.stock_id
     AND item.deleted=b'0'
       SET source.reservation_item_id=item.id;
    SELECT COUNT(*) INTO v_count FROM tmp_v2_new_owner
     WHERE reservation_id IS NULL OR reservation_item_id IS NULL;
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ModernWMS owner主键回读失败';
    END IF;

    INSERT INTO wms_erp_stock_reservation_allocation
        (tenant_id,reservation_item_id,erp_stock_id,stock_allocation_id,
         reserved_qty,released_qty,consumed_qty,remaining_qty,status,row_version,
         creator,create_time,updater,update_time,deleted)
    SELECT 1,reservation_item_id,stock_id,allocation_id,0,0,0,0,'ACTIVE',0,
           'reservation-cutover-v2',NOW(6),'reservation-cutover-v2',NOW(6),b'0'
      FROM tmp_v2_new_owner;
    IF ROW_COUNT()<>7 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ModernWMS allocation owner必须插入7行';
    END IF;

    -- 每条永久来源按共享最小事务契约真实RESERVE。
    SELECT MAX(seq) INTO v_max_seq FROM tmp_v2_new_owner;
    SET v_seq=1;
    WHILE v_seq<=v_max_seq DO
        SELECT source_kind,source_row_id,task_id,task_no,source_item_id,stock_id,
               allocation_id,action_qty,reservation_id,reservation_item_id,source_line_key
          INTO v_source_kind,v_source_row_id,v_task_id,v_task_no,v_source_item_id,v_stock_id,
               v_allocation_id,v_action_qty,v_reservation_id,v_reservation_item_id,v_source_line_key
          FROM tmp_v2_new_owner WHERE seq=v_seq;

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
          FROM trk_stock
         WHERE id=v_stock_id AND warehouse_id=320118 AND deleted=b'0'
         FOR UPDATE;
        SELECT occupied_qty INTO v_before_allocation_occupied
          FROM wms_erp_stock_allocation
         WHERE id=v_allocation_id AND tenant_id=1 AND erp_stock_id=v_stock_id
           AND location_state='UNLOCATED' AND goods_location_id IS NULL
         FOR UPDATE;
        IF v_before_available<v_action_qty
           OR v_before_total<>v_before_available+v_before_occupied
           OR v_before_allocation_occupied+v_action_qty>
              (SELECT allocated_qty FROM wms_erp_stock_allocation WHERE id=v_allocation_id) THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='WMS新预占可用量或allocation不足';
        END IF;

        SET v_command_id=CONCAT('MIGRATE_V2:',v_source_kind,':',v_source_row_id);
        SET v_operation_key=CONCAT('MIG-WMS-V2:',LEFT(SHA2(CONCAT(
            v_source_kind,':',v_source_row_id,':',v_stock_id,':',v_allocation_id,':',v_action_qty),256),51));
        SET v_request_fingerprint=SHA2(CONCAT_WS('|','WMS_RESERVATION_MIGRATION_V2',
            v_command_id,'RESERVE',v_reservation_id,v_reservation_item_id,v_stock_id,
            v_allocation_id,v_action_qty,
            SHA2(CONCAT(v_stock_id,'|',v_allocation_id,'|',v_action_qty),256)),256);

        INSERT INTO trk_stock_reservation_command
            (namespace,command_id,action,reservation_id,request_fingerprint,result_status,
             operator_id,operator_name,version,creator,create_time,updater,update_time,deleted,tenant_id)
        VALUES ('WMS_RESERVATION_MIGRATION_V2',v_command_id,'RESERVE',v_reservation_id,
                v_request_fingerprint,'PENDING',0,'reservation-cutover-v2',0,
                'reservation-cutover-v2',NOW(),'reservation-cutover-v2',NOW(),b'0',1);
        SET v_command_header_id=LAST_INSERT_ID();

        INSERT INTO trk_stock_reservation_command_item
            (command_header_id,line_no,reservation_id,reservation_item_id,source_line_key,
             stock_id,action_qty,expected_reservation_version,expected_item_version,
             allocation_plan_fingerprint,request_line_fingerprint,
             creator,create_time,updater,update_time,deleted,tenant_id)
        VALUES (v_command_header_id,1,v_reservation_id,v_reservation_item_id,v_source_line_key,
                v_stock_id,v_action_qty,v_reservation_version,0,
                SHA2(CONCAT(v_stock_id,'|',v_allocation_id,'|',v_action_qty),256),
                v_request_fingerprint,'reservation-cutover-v2',NOW(),
                'reservation-cutover-v2',NOW(),b'0',1);

        INSERT INTO wms_inventory_operation
            (tenant_id,operation_key,shared_command_id,reservation_id,reservation_item_id,
             biz_type,biz_id,biz_item_id,mutation_type,erp_stock_id,allocation_id,
             counterpart_allocation_id,quantity,operator,result_status,create_time,update_time)
        VALUES (1,v_operation_key,v_command_header_id,v_reservation_id,v_reservation_item_id,
                'PACKING_LOCK',v_task_id,v_source_item_id,'RESERVE',v_stock_id,v_allocation_id,
                NULL,v_action_qty,'reservation-cutover-v2','PENDING',NOW(6),NOW(6));

        UPDATE trk_stock_reservation_item
           SET reserved_qty=v_action_qty,remaining_qty=v_action_qty,status='ACTIVE',version=1,
               updater='reservation-cutover-v2',update_time=NOW()
         WHERE id=v_reservation_item_id AND reservation_id=v_reservation_id
           AND reserved_qty=0 AND released_qty=0 AND consumed_qty=0
           AND remaining_qty=0 AND version=0;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='reservation item预占CAS失败';
        END IF;

        UPDATE wms_erp_stock_reservation_allocation
           SET reserved_qty=v_action_qty,remaining_qty=v_action_qty,status='ACTIVE',row_version=1,
               updater='reservation-cutover-v2',update_time=NOW(6)
         WHERE id=v_allocation_reservation_id AND reserved_qty=0 AND released_qty=0
           AND consumed_qty=0 AND remaining_qty=0 AND row_version=0;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='allocation owner预占CAS失败';
        END IF;

        UPDATE trk_stock_reservation
           SET status='ACTIVE',close_mode=NULL,version=version+1,
               updater='reservation-cutover-v2',update_time=NOW()
         WHERE tenant_id=1 AND id=v_reservation_id AND version=v_reservation_version
           AND status='ACTIVE' AND close_mode IS NULL AND deleted=b'0';
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='reservation master版本推进失败';
        END IF;

        UPDATE trk_stock
           SET available_qty=v_before_available-v_action_qty,
               occupied_qty=v_before_occupied+v_action_qty,
               updater='reservation-cutover-v2',update_time=NOW()
         WHERE id=v_stock_id AND available_qty=v_before_available
           AND occupied_qty=v_before_occupied AND total_qty=v_before_total;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ERP库存预占CAS失败';
        END IF;

        UPDATE wms_erp_stock_allocation
           SET occupied_qty=v_before_allocation_occupied+v_action_qty,
               row_version=row_version+1,updater='reservation-cutover-v2',update_time=NOW(6)
         WHERE id=v_allocation_id AND occupied_qty=v_before_allocation_occupied;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='allocation预占CAS失败';
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
        SELECT CONCAT('MIG-WMS-V2-',v_source_kind,'-',v_source_row_id),
               'PACKING_LOCK',v_task_id,v_source_item_id,v_task_no,stock.id,
               stock.freight_forwarder_id,stock.warehouse_id,stock.dept_id,stock.order_user_id,
               stock.commodity_id,stock.commodity_sku,stock.commodity_name,v_operation_key,
               v_command_header_id,v_reservation_id,v_reservation_item_id,'RESERVE',
               -v_action_qty,v_action_qty,0,
               v_before_available,v_before_available-v_action_qty,
               v_before_occupied,v_before_occupied+v_action_qty,
               v_before_total,v_before_total,0,v_before_available,v_before_available-v_action_qty,
               'TRANSFER',NOW(),0,'reservation-cutover-v2','WMS永久来源迁移真实RESERVE',
               'reservation-cutover-v2',NOW(),'reservation-cutover-v2',NOW(),b'0'
          FROM trk_stock stock WHERE stock.id=v_stock_id;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='WMS RESERVE库存流水插入失败';
        END IF;
        SET v_stock_record_id=LAST_INSERT_ID();
        SET v_result_fingerprint=SHA2(CONCAT(v_request_fingerprint,'|',v_stock_id,'|',
            v_action_qty,'|',v_before_available-v_action_qty,'|',v_before_occupied+v_action_qty),256);

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
               'reservation-cutover-v2',NOW(6),'WMS独立永久来源真实RESERVE'
          FROM wms_erp_stock_allocation allocation WHERE allocation.id=v_allocation_id;

        UPDATE trk_stock_reservation_command_item
           SET stock_record_id=v_stock_record_id,result_remaining_qty=v_action_qty,
               result_line_fingerprint=SHA2(CONCAT(v_command_header_id,'|',
                   v_reservation_item_id,'|',v_stock_record_id,'|',v_action_qty),256),
               updater='reservation-cutover-v2',update_time=NOW()
         WHERE command_header_id=v_command_header_id AND reservation_item_id=v_reservation_item_id;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='shared command item完成失败';
        END IF;

        UPDATE trk_stock_reservation_command
           SET result_status='SUCCEEDED',result_fingerprint=v_result_fingerprint,
               complete_time=NOW(),version=1,updater='reservation-cutover-v2',update_time=NOW()
         WHERE id=v_command_header_id AND result_status='PENDING' AND version=0;
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='shared command完成失败';
        END IF;
        UPDATE wms_inventory_operation
           SET result_status='SUCCEEDED',erp_stock_record_id=v_stock_record_id,update_time=NOW(6)
         WHERE tenant_id=1 AND operation_key=v_operation_key AND result_status='PENDING';
        IF ROW_COUNT()<>1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='local command完成失败';
        END IF;
        SET v_seq=v_seq+1;
    END WHILE;

    -- 回填7条新来源的永久业务引用。
    UPDATE wms_packing_task_stock_selection selection
    JOIN tmp_v2_new_owner source
      ON source.source_kind='PACKING_SELECTION' AND source.source_row_id=selection.id
       SET selection.erp_stock_id=source.stock_id,
           selection.stock_allocation_id=source.allocation_id,
           selection.reservation_id=source.reservation_id,
           selection.reservation_item_id=source.reservation_item_id,
           selection.stock_id=0,selection.goods_location_id=NULL
     WHERE selection.tenant_id=1 AND selection.id IN (1,2,3,4,6,7)
       AND selection.reservation_id IS NULL AND selection.reservation_item_id IS NULL;
    IF ROW_COUNT()<>6 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='6条selection引用迁移失败';
    END IF;
    UPDATE wms_dispatchpicklist pick
    JOIN tmp_v2_new_owner source
      ON source.source_kind='PENDING_PICK' AND source.source_row_id=pick.id
       SET pick.erp_stock_id=source.stock_id,pick.stock_allocation_id=source.allocation_id,
           pick.reservation_id=source.reservation_id,pick.reservation_item_id=source.reservation_item_id,
           pick.stock_id=0,pick.goods_location_id=NULL
     WHERE pick.id=5 AND pick.is_update_stock=0
       AND pick.reservation_id IS NULL AND pick.reservation_item_id IS NULL;
    IF ROW_COUNT()<>1 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='pick5引用迁移失败';
    END IF;

    -- 96条历史收货保留快照/erp_stock，迁移primary allocation并清旧stock引用。
    SELECT COUNT(*) INTO v_count
      FROM wms_erp_receipt_item item
      JOIN tmp_v2_legacy_map map ON map.legacy_stock_id=item.wms_stock_id
     WHERE item.tenant_id=1 AND item.erp_stock_id=map.erp_stock_id
       AND item.primary_stock_allocation_id IS NULL;
    IF v_count<>96 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='历史收货待迁移引用必须为96条';
    END IF;
    UPDATE wms_erp_receipt_item item
    JOIN tmp_v2_legacy_map map ON map.legacy_stock_id=item.wms_stock_id
    JOIN wms_erp_stock_allocation allocation
      ON allocation.tenant_id=1 AND allocation.erp_stock_id=map.erp_stock_id
       SET item.primary_stock_allocation_id=allocation.id,item.wms_stock_id=NULL
     WHERE item.tenant_id=1 AND item.erp_stock_id=map.erp_stock_id
       AND item.primary_stock_allocation_id IS NULL;
    IF ROW_COUNT()<>96 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='历史收货引用迁移必须更新96条';
    END IF;

    -- 四层守恒及最终仓汇总。
    SELECT COUNT(*) INTO v_count
      FROM trk_stock stock
      LEFT JOIN (
          SELECT item.stock_id,SUM(item.remaining_qty) remaining_qty
            FROM trk_stock_reservation_item item
           WHERE item.tenant_id=1 AND item.deleted=b'0'
           GROUP BY item.stock_id
      ) owner ON owner.stock_id=stock.id
     WHERE stock.warehouse_id=320118 AND stock.deleted=b'0'
       AND stock.occupied_qty<>COALESCE(owner.remaining_qty,0);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ERP occupied与reservation item不守恒';
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
       AND (stock.total_qty<>COALESCE(allocation.allocated_qty,0)
         OR stock.occupied_qty<>COALESCE(allocation.occupied_qty,0)
         OR stock.available_qty<>COALESCE(allocation.allocated_qty,0)-COALESCE(allocation.occupied_qty,0));
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='allocation与ERP三分量不守恒';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM trk_stock_reservation_item item
      JOIN trk_stock stock ON stock.id=item.stock_id
      LEFT JOIN (
          SELECT reservation_item_id,SUM(remaining_qty) remaining_qty
            FROM wms_erp_stock_reservation_allocation
           WHERE tenant_id=1 AND deleted=b'0' GROUP BY reservation_item_id
      ) owner ON owner.reservation_item_id=item.id
     WHERE item.tenant_id=1 AND item.deleted=b'0' AND stock.warehouse_id=320118
       AND item.remaining_qty<>COALESCE(owner.remaining_qty,0);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='reservation item与allocation owner不守恒';
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM wms_erp_stock_allocation allocation
      JOIN trk_stock stock ON stock.id=allocation.erp_stock_id
      LEFT JOIN (
          SELECT stock_allocation_id,SUM(remaining_qty) remaining_qty
            FROM wms_erp_stock_reservation_allocation
           WHERE tenant_id=1 AND deleted=b'0' GROUP BY stock_allocation_id
      ) owner ON owner.stock_allocation_id=allocation.id
     WHERE allocation.tenant_id=1 AND stock.warehouse_id=320118 AND stock.deleted=b'0'
       AND allocation.occupied_qty<>COALESCE(owner.remaining_qty,0);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='allocation occupied与allocation owner不守恒';
    END IF;

    SELECT COUNT(*),COALESCE(SUM(available_qty),0),COALESCE(SUM(occupied_qty),0),COALESCE(SUM(total_qty),0)
      INTO v_count,v_available,v_occupied,v_total
      FROM trk_stock WHERE warehouse_id=320118 AND deleted=b'0';
    IF v_count<>95 OR v_available<>56838 OR v_occupied<>4300 OR v_total<>61138 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='最终必须为95/56838/4300/61138';
    END IF;

    -- 删除前证明所有活跃业务不再引用目标旧库存；wms_stock_record作为历史审计保留。
    SELECT
      (SELECT COUNT(*) FROM wms_dispatchpicklist pick
        JOIN tmp_v2_legacy_map map ON map.legacy_stock_id=pick.stock_id)
      +(SELECT COUNT(*) FROM wms_packing_task_stock_selection selection
        JOIN tmp_v2_legacy_map map ON map.legacy_stock_id=selection.stock_id
        WHERE selection.tenant_id=1)
      +(SELECT COUNT(*) FROM wms_erp_receipt_item item
        JOIN tmp_v2_legacy_map map ON map.legacy_stock_id=item.wms_stock_id
        WHERE item.tenant_id=1)
      INTO v_count;
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='仍有pick/selection/receipt引用目标wms_stock';
    END IF;

    SELECT COUNT(*) INTO v_count FROM wms_dispatchpicklist
     WHERE id IN (1,2,3,4,5) AND is_update_stock=0
       AND (stock_id<>0 OR goods_location_id IS NOT NULL OR erp_stock_id IS NULL
         OR stock_allocation_id IS NULL OR reservation_id IS NULL OR reservation_item_id IS NULL);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='5条pick未完整切换ERP引用';
    END IF;
    SELECT COUNT(*) INTO v_count FROM wms_packing_task_stock_selection
     WHERE tenant_id=1 AND id IN (1,2,3,4,6,7)
       AND (stock_id<>0 OR goods_location_id IS NOT NULL OR erp_stock_id IS NULL
         OR stock_allocation_id IS NULL OR reservation_id IS NULL OR reservation_item_id IS NULL);
    IF v_count<>0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='6条selection未完整切换ERP引用';
    END IF;

    DELETE legacy
      FROM wms_stock legacy
      JOIN tmp_v2_legacy_map map ON map.legacy_stock_id=legacy.id
     WHERE legacy.tenant_id=1;
    IF ROW_COUNT()<>92 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='目标wms_stock必须精确删除92行';
    END IF;

    UPDATE wms_inventory_runtime_config
       SET mode='CANONICAL_ERP',maintenance_enabled=0,cutover_time=NOW(6),
           row_version=row_version+1,updater='reservation-cutover-v2',update_time=NOW(6)
     WHERE tenant_id=1 AND erp_warehouse_id=320118
       AND mode='LEGACY_READ' AND maintenance_enabled=1;
    IF ROW_COUNT()<>1 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='runtime切换CANONICAL_ERP失败';
    END IF;

    COMMIT;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_new_owner;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_old_pick_owner;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_erp_owner;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_allocation_source;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_missing_area;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_missing_area_candidate;
    DROP TEMPORARY TABLE IF EXISTS tmp_v2_legacy_map;
END$$

DELIMITER ;

-- 安全默认：只创建过程，不执行。维护窗口确认后人工取消下一行注释。
-- CALL `wms_cutover_reservation_320118_20260821_v2`();

-- 验收完成后可删除过程；保留期间完整重放只做最终态校验。
-- DROP PROCEDURE IF EXISTS `wms_cutover_reservation_320118_20260821_v2`;
