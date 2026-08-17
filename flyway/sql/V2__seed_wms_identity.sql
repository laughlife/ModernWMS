-- Deterministic WMS authorization seed extracted from backend/ModernWMS/SeedData.
-- Existing rows are matched by primary key and are never overwritten.
-- User credentials are intentionally not seeded. Provision the first administrator explicitly.

INSERT INTO `wms_userrole`
    (`id`,`role_name`,`is_valid`,`create_time`,`last_update_time`,`tenant_id`)
SELECT 1,'admin',1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1
WHERE NOT EXISTS (SELECT 1 FROM `wms_userrole` WHERE `id`=1);

INSERT INTO `wms_menu`
    (`id`,`menu_name`,`module`,`vue_path`,`vue_path_detail`,`vue_directory`,`sort`,`tenant_id`,`menu_actions`)
SELECT seed.* FROM (
    SELECT 20,'companySetting','baseModule','companySetting','','base/companySetting',1,1,'[]'
    UNION ALL SELECT 21,'userRoleSetting','baseModule','userRoleSetting','','base/userRoleSetting',2,1,'[]'
    UNION ALL SELECT 22,'roleMenu','baseModule','roleMenu','','base/roleMenu',3,1,'[]'
    UNION ALL SELECT 23,'userManagement','baseModule','userManagement','','base/userManagement',4,1,'[]'
    UNION ALL SELECT 24,'commodityCategorySetting','baseModule','commodityCategorySetting','','base/commodityCategorySetting',5,1,'[]'
    UNION ALL SELECT 25,'commodityManagement','baseModule','commodityManagement','','base/commodityManagement',6,1,'[]'
    UNION ALL SELECT 26,'supplier','baseModule','supplier','','base/supplier',7,1,'[]'
    UNION ALL SELECT 27,'warehouseSetting','baseModule','warehouseSetting','','base/warehouseSetting',8,1,'[]'
    UNION ALL SELECT 28,'ownerOfCargo','baseModule','ownerOfCargo','','base/ownerOfCargo',9,1,'[]'
    UNION ALL SELECT 29,'freightSetting','baseModule','freightSetting','','base/freightSetting',10,1,'[]'
    UNION ALL SELECT 31,'stockManagement','','stockManagement','','wms/stockManagement',12,1,'[]'
    UNION ALL SELECT 32,'warehouseProcessing','warehouseWorkingModule','warehouseProcessing','','warehouseWorking/warehouseProcessing',13,1,'[]'
    UNION ALL SELECT 33,'warehouseMove','warehouseWorkingModule','warehouseMove','','warehouseWorking/warehouseMove',14,1,'[]'
    UNION ALL SELECT 34,'warehouseFreeze','warehouseWorkingModule','warehouseFreeze','','warehouseWorking/warehouseFreeze',15,1,'[]'
    UNION ALL SELECT 35,'warehouseAdjust','warehouseWorkingModule','warehouseAdjust','','warehouseWorking/warehouseAdjust',16,1,'[]'
    UNION ALL SELECT 36,'warehouseTaking','warehouseWorkingModule','warehouseTaking','','warehouseWorking/warehouseTaking',17,1,'[]'
    UNION ALL SELECT 37,'stockAsn','','stockAsn','','wms/stockAsn',18,1,'[]'
    UNION ALL SELECT 38,'deliveryManagement','deliveryManagement','deliveryManagement','','deliveryManagement/deliveryManagement',19,1,'[]'
) AS seed (`id`,`menu_name`,`module`,`vue_path`,`vue_path_detail`,`vue_directory`,`sort`,`tenant_id`,`menu_actions`)
LEFT JOIN `wms_menu` existing ON existing.`id`=seed.`id`
WHERE existing.`id` IS NULL;

INSERT INTO `wms_rolemenu`
    (`id`,`userrole_id`,`menu_id`,`authority`,`create_time`,`last_update_time`,`tenant_id`,`menu_actions_authority`)
SELECT seed.* FROM (
    SELECT 1,1,20,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 2,1,21,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 3,1,22,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 4,1,23,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 5,1,24,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 6,1,25,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 7,1,26,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 8,1,27,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 9,1,28,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 10,1,29,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 12,1,31,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 13,1,32,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 14,1,33,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 15,1,34,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 16,1,35,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 17,1,36,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 18,1,37,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
    UNION ALL SELECT 19,1,38,1,'2023-01-06 14:14:34.328193','2023-01-06 14:14:34.328193',1,'[]'
) AS seed (`id`,`userrole_id`,`menu_id`,`authority`,`create_time`,`last_update_time`,`tenant_id`,`menu_actions_authority`)
LEFT JOIN `wms_rolemenu` existing ON existing.`id`=seed.`id`
WHERE existing.`id` IS NULL;
