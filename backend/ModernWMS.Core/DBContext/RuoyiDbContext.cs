using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext.Entities;

namespace ModernWMS.Core.DBContext;

/// <summary>
/// Ruoyi/ERP 现有业务表上下文。
/// 与 WMS 上下文共用同一个 ruoyi-vue-pro 数据库连接，Ruoyi 表按业务需要显式映射。
/// </summary>
public sealed class RuoyiDbContext : DbContext
{
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="options">上下文选项</param>
    public RuoyiDbContext(DbContextOptions<RuoyiDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// 系统部门
    /// </summary>
    public DbSet<ErpSystemDeptEntity> SystemDepts => Set<ErpSystemDeptEntity>();

    /// <summary>
    /// ModernWMS warehouse and ERP operator-group bindings.
    /// </summary>
    public DbSet<ErpWarehouseOperatorGroupEntity> WarehouseOperatorGroups => Set<ErpWarehouseOperatorGroupEntity>();

    /// <summary>
    /// 系统用户
    /// </summary>
    public DbSet<ErpSystemUserEntity> SystemUsers => Set<ErpSystemUserEntity>();

    /// <summary>
    /// 供应商
    /// </summary>
    public DbSet<ErpSupplierEntity> Suppliers => Set<ErpSupplierEntity>();

    /// <summary>
    /// ERP 仓库
    /// </summary>
    public DbSet<ErpWarehouseEntity> Warehouses => Set<ErpWarehouseEntity>();

    /// <summary>
    /// ERP logistics shipment facts.
    /// </summary>
    public DbSet<ErpLogisticsInfoEntity> LogisticsInfos => Set<ErpLogisticsInfoEntity>();

    /// <summary>
    /// ModernWMS receipt records used to exclude already-confirmed ERP shipments.
    /// </summary>
    public DbSet<ErpReceiptRecordEntity> ReceiptRecords => Set<ErpReceiptRecordEntity>();

    /// <summary>
    /// Product-level WMS receipt results.
    /// </summary>
    public DbSet<ErpReceiptItemEntity> ReceiptItems => Set<ErpReceiptItemEntity>();

    /// <summary>
    /// ERP commodity to WMS master-data mappings.
    /// </summary>
    public DbSet<ErpCommodityMapEntity> CommodityMaps => Set<ErpCommodityMapEntity>();

    /// <summary>
    /// ERP commodity master data (sku, name, product image).
    /// </summary>
    public DbSet<ErpCommodityEntity> Commodities => Set<ErpCommodityEntity>();

    /// <summary>
    /// ERP ownership to WMS goods-owner mappings.
    /// </summary>
    public DbSet<ErpGoodsOwnerMapEntity> GoodsOwnerMaps => Set<ErpGoodsOwnerMapEntity>();

    /// <summary>
    /// WMS physical inventory ledger.
    /// </summary>
    public DbSet<WmsStockRecordEntity> WmsStockRecords => Set<WmsStockRecordEntity>();

    /// <summary>
    /// ERP logistics tracking snapshots.
    /// </summary>
    public DbSet<ErpTrackEntity> Tracks => Set<ErpTrackEntity>();

    /// <summary>
    /// ERP logistics tracking events.
    /// </summary>
    public DbSet<ErpTrackEventEntity> TrackEvents => Set<ErpTrackEventEntity>();

    /// <summary>
    /// ERP 文件存储配置。
    /// </summary>
    public DbSet<ErpFileConfigEntity> FileConfigs => Set<ErpFileConfigEntity>();

    /// <summary>
    /// ERP FBA shipment preparation headers.
    /// </summary>
    public DbSet<ErpStockMoveEntity> StockMoves => Set<ErpStockMoveEntity>();

    /// <summary>
    /// ERP FBA shipment preparation items.
    /// </summary>
    public DbSet<ErpStockMoveItemEntity> StockMoveItems => Set<ErpStockMoveItemEntity>();

    /// <summary>
    /// ERP business stock pools referenced by FBA preparations.
    /// </summary>
    public DbSet<ErpBusinessStockEntity> BusinessStocks => Set<ErpBusinessStockEntity>();

    /// <summary>
    /// ERP FBA shipment headers.
    /// </summary>
    public DbSet<ErpFbaShipmentEntity> FbaShipments => Set<ErpFbaShipmentEntity>();

    /// <summary>
    /// ERP FBA shipment boxes and tracking numbers.
    /// </summary>
    public DbSet<ErpFbaSpdBoxEntity> FbaShipmentBoxes => Set<ErpFbaSpdBoxEntity>();

    /// <summary>
    /// ERP FBA shipment items with the authoritative FN SKU and product image.
    /// </summary>
    public DbSet<ErpFbaShipmentItemEntity> FbaShipmentItems => Set<ErpFbaShipmentItemEntity>();

    /// <summary>
    /// 显式映射当前已接入 ModernWMS 的 Ruoyi 业务实体。
    /// </summary>
    /// <param name="modelBuilder">model builder</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ErpSystemDeptEntity>(entity =>
        {
            entity.ToTable("system_dept");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpWarehouseOperatorGroupEntity>(entity =>
        {
            entity.ToTable("wms_warehouse_operator_group");
            entity.HasKey(t => t.id);
            entity.HasIndex(t => new { t.tenant_id, t.warehouse_id, t.dept_id }).IsUnique();
            entity.Property(t => t.id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<ErpSystemUserEntity>(entity =>
        {
            entity.ToTable("system_users");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpSupplierEntity>(entity =>
        {
            entity.ToTable("erp_supplier");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpWarehouseEntity>(entity =>
        {
            entity.ToTable("erp_warehouse");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpLogisticsInfoEntity>(entity =>
        {
            entity.ToTable("trk_logistics_info");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpReceiptRecordEntity>(entity =>
        {
            entity.ToTable("wms_erp_receipt");
            entity.HasKey(t => t.id);
            entity.HasIndex(t => t.shipment_id).IsUnique();
        });

        modelBuilder.Entity<ErpReceiptItemEntity>(entity =>
        {
            entity.ToTable("wms_erp_receipt_item");
            entity.HasKey(t => t.id);
            entity.HasIndex(t => new { t.receipt_id, t.source_item_key }).IsUnique();
        });

        modelBuilder.Entity<ErpCommodityMapEntity>(entity =>
        {
            entity.ToTable("wms_erp_commodity_map");
            entity.HasKey(t => t.id);
            entity.HasIndex(t => new { t.tenant_id, t.erp_commodity_id }).IsUnique();
        });

        modelBuilder.Entity<ErpCommodityEntity>(entity =>
        {
            entity.ToTable("erp_commodity");
            entity.HasKey(t => t.id);
            entity.Property(t => t.id).HasMaxLength(64);
            entity.Property(t => t.sku).HasMaxLength(100);
            entity.Property(t => t.name).HasMaxLength(255);
        });

        modelBuilder.Entity<ErpGoodsOwnerMapEntity>(entity =>
        {
            entity.ToTable("wms_erp_goods_owner_map");
            entity.HasKey(t => t.id);
            entity.HasIndex(t => new { t.tenant_id, t.erp_dept_id, t.erp_order_user_id })
                .HasDatabaseName("UX_wms_owner_map_erp_owner")
                .IsUnique();
        });

        modelBuilder.Entity<WmsStockRecordEntity>(entity =>
        {
            entity.ToTable("wms_stock_record");
            entity.HasKey(t => t.id);
            entity.HasIndex(t => new { t.biz_type, t.biz_id, t.biz_item_id, t.stock_id })
                .HasDatabaseName("UX_wms_stock_record_biz")
                .IsUnique();
        });

        modelBuilder.Entity<ErpTrackEntity>(entity =>
        {
            entity.ToTable("trk_track");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpTrackEventEntity>(entity =>
        {
            entity.ToTable("trk_track_event");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpFileConfigEntity>(entity =>
        {
            entity.ToTable("infra_file_config");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpStockMoveEntity>(entity =>
        {
            entity.ToTable("trk_stock_move");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpStockMoveItemEntity>(entity =>
        {
            entity.ToTable("trk_stock_move_item");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpBusinessStockEntity>(entity =>
        {
            entity.ToTable("trk_stock");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpFbaShipmentEntity>(entity =>
        {
            entity.ToTable("erp_fba_shipment");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpFbaSpdBoxEntity>(entity =>
        {
            entity.ToTable("erp_fba_spd_box");
            entity.HasKey(t => t.id);
        });

        modelBuilder.Entity<ErpFbaShipmentItemEntity>(entity =>
        {
            entity.ToTable("erp_fba_shipment_item");
            entity.HasKey(t => t.id);
        });

        base.OnModelCreating(modelBuilder);
    }
}
