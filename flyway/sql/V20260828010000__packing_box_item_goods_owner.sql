-- A packed quantity must retain the contributing goods owner so Ruoyi can consume the same owner contribution.
ALTER TABLE `wms_weighing_box_item`
  ADD COLUMN `goods_owner_id` INT NOT NULL DEFAULT 0 AFTER `packing_task_item_id`,
  ADD KEY `idx_weighing_box_item_owner` (`goods_owner_id`);

-- Existing physical-box rows cannot be attributed safely. They remain readable but must not be confirmed by the new flow.
