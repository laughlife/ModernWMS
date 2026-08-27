-- ERP/WMS 已取消租户概念：重建不含 tenant_id 的业务索引并删除物理列。
-- 发布前必须确认同名业务键在去除 tenant_id 后不存在重复；迁移不会合并或改写业务数据。

DELIMITER $$

CREATE PROCEDURE `mwms_remove_tenant_dependencies`()
BEGIN
    DECLARE done INT DEFAULT 0;
    DECLARE table_name_value VARCHAR(64);
    DECLARE index_name_value VARCHAR(64);
    DECLARE non_unique_value INT;
    DECLARE columns_value TEXT;
    DECLARE replacement_name VARCHAR(64);

    DECLARE index_cursor CURSOR FOR
        SELECT s.TABLE_NAME,
               s.INDEX_NAME,
               MIN(s.NON_UNIQUE),
               GROUP_CONCAT(
                   CASE WHEN s.COLUMN_NAME <> 'tenant_id' THEN
                       CONCAT('`', REPLACE(s.COLUMN_NAME, '`', '``'), '`',
                              IF(s.SUB_PART IS NULL, '', CONCAT('(', s.SUB_PART, ')')))
                   END
                   ORDER BY s.SEQ_IN_INDEX SEPARATOR ',')
          FROM information_schema.STATISTICS s
         WHERE s.TABLE_SCHEMA = DATABASE()
           AND s.INDEX_NAME <> 'PRIMARY'
           AND s.TABLE_NAME IN (
               'trk_stock_reservation','trk_stock_reservation_command',
               'trk_stock_reservation_command_item','trk_stock_reservation_item',
               'wms_action_log','wms_asn','wms_asnmaster','wms_asnsort','wms_company',
               'wms_dispatch_order','wms_dispatch_recovery','wms_dispatch_recovery_result',
               'wms_dispatch_weighing_box','wms_dispatchlist','wms_erp_commodity_map',
               'wms_erp_goods_owner_map','wms_erp_receipt','wms_erp_receipt_item',
               'wms_erp_stock_allocation','wms_erp_stock_allocation_log',
               'wms_erp_stock_reservation_allocation','wms_flowsetmain','wms_freightfee',
               'wms_global_unique_serial','wms_goodslocation','wms_goodsowner',
               'wms_inventory_operation','wms_inventory_runtime_config','wms_menu',
               'wms_packing_task_stock_selection','wms_receipt_item_owner','wms_role_warehouse',
               'wms_rolemenu','wms_spu','wms_stock','wms_stock_record','wms_stockadjust',
               'wms_stockfreeze','wms_stockmove','wms_stockprocess','wms_stockprocessdetail',
               'wms_stocktaking','wms_supplier','wms_user','wms_user_defined_print_solution',
               'wms_userrole','wms_warehouse','wms_warehouse_operator_group','wms_warehousearea',
               'wms_warehousearea_operator_group')
         GROUP BY s.TABLE_NAME, s.INDEX_NAME
        HAVING SUM(s.COLUMN_NAME = 'tenant_id') > 0;

    DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1;

    OPEN index_cursor;
    index_loop: LOOP
        FETCH index_cursor INTO table_name_value, index_name_value, non_unique_value, columns_value;
        IF done = 1 THEN
            LEAVE index_loop;
        END IF;

        IF columns_value IS NOT NULL AND columns_value <> '' THEN
            SET replacement_name = CONCAT(
                IF(non_unique_value = 0, 'ux_business_', 'ix_business_'),
                LEFT(SHA2(CONCAT(table_name_value, ':', index_name_value), 256), 16));
            SET @sql_text = CONCAT(
                'CREATE ', IF(non_unique_value = 0, 'UNIQUE ', ''), 'INDEX `', replacement_name,
                '` ON `', REPLACE(table_name_value, '`', '``'), '` (', columns_value, ')');
            PREPARE statement_handle FROM @sql_text;
            EXECUTE statement_handle;
            DEALLOCATE PREPARE statement_handle;
        END IF;

        SET @sql_text = CONCAT('DROP INDEX `', REPLACE(index_name_value, '`', '``'),
                               '` ON `', REPLACE(table_name_value, '`', '``'), '`');
        PREPARE statement_handle FROM @sql_text;
        EXECUTE statement_handle;
        DEALLOCATE PREPARE statement_handle;
    END LOOP;
    CLOSE index_cursor;

    SET done = 0;
    BEGIN
        DECLARE column_cursor CURSOR FOR
            SELECT c.TABLE_NAME
              FROM information_schema.COLUMNS c
             WHERE c.TABLE_SCHEMA = DATABASE()
               AND c.COLUMN_NAME = 'tenant_id'
               AND c.TABLE_NAME IN (
                   'trk_stock_reservation','trk_stock_reservation_command',
                   'trk_stock_reservation_command_item','trk_stock_reservation_item',
                   'wms_action_log','wms_asn','wms_asnmaster','wms_asnsort','wms_company',
                   'wms_dispatch_order','wms_dispatch_recovery','wms_dispatch_recovery_result',
                   'wms_dispatch_weighing_box','wms_dispatchlist','wms_erp_commodity_map',
                   'wms_erp_goods_owner_map','wms_erp_receipt','wms_erp_receipt_item',
                   'wms_erp_stock_allocation','wms_erp_stock_allocation_log',
                   'wms_erp_stock_reservation_allocation','wms_flowsetmain','wms_freightfee',
                   'wms_global_unique_serial','wms_goodslocation','wms_goodsowner',
                   'wms_inventory_operation','wms_inventory_runtime_config','wms_menu',
                   'wms_packing_task_stock_selection','wms_receipt_item_owner','wms_role_warehouse',
                   'wms_rolemenu','wms_spu','wms_stock','wms_stock_record','wms_stockadjust',
                   'wms_stockfreeze','wms_stockmove','wms_stockprocess','wms_stockprocessdetail',
                   'wms_stocktaking','wms_supplier','wms_user','wms_user_defined_print_solution',
                   'wms_userrole','wms_warehouse','wms_warehouse_operator_group','wms_warehousearea',
                   'wms_warehousearea_operator_group');
        DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1;

        OPEN column_cursor;
        column_loop: LOOP
            FETCH column_cursor INTO table_name_value;
            IF done = 1 THEN
                LEAVE column_loop;
            END IF;
            SET @sql_text = CONCAT('ALTER TABLE `', REPLACE(table_name_value, '`', '``'),
                                   '` DROP COLUMN `tenant_id`');
            PREPARE statement_handle FROM @sql_text;
            EXECUTE statement_handle;
            DEALLOCATE PREPARE statement_handle;
        END LOOP;
        CLOSE column_cursor;
    END;
END$$

CALL `mwms_remove_tenant_dependencies`()$$
DROP PROCEDURE `mwms_remove_tenant_dependencies`$$

DELIMITER ;
