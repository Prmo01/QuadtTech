using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MauiApp2.Models;
using MauiApp2.Components.Database;

namespace MauiApp2.Services
{
    public interface IPurchaseOrderService
    {
        Task<List<PurchaseOrder>> GetPendingPurchaseOrdersAsync();
        Task<List<PurchaseOrder>> GetAllPurchaseOrdersAsync();
        Task<PurchaseOrder> GetPurchaseOrderByIdAsync(int poId);
        Task<List<PurchaseOrderItem>> GetPurchaseOrderItemsAsync(int poId);
        Task<bool> UpdatePurchaseOrderStatusAsync(int poId, string status);
        Task<int> CreatePurchaseOrderAsync(int supplierId, DateTime orderDate, DateTime expectedDate, string? notes, List<PurchaseOrderItem> items);
    }

    public class PurchaseOrderService : IPurchaseOrderService
    {
        // Get pending purchase orders (for Stock In)
        public async Task<List<PurchaseOrder>> GetPendingPurchaseOrdersAsync()
        {
            var purchaseOrders = new List<PurchaseOrder>();

            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var command = new SqlCommand(@"
                    SELECT po.po_id, po.supplier_id, po.po_number, po.order_date, po.expected_date, 
                           po.status, po.total_amount, po.notes, po.created_date, po.modified_date,
                           s.supplier_name
                    FROM tbl_purchase_order po
                    LEFT JOIN tbl_supplier s ON po.supplier_id = s.supplier_id
                    LEFT JOIN tbl_stock_in si ON po.po_id = si.po_id
                    WHERE po.status = 'Delivered' 
                      AND si.po_id IS NULL
                    ORDER BY po.order_date DESC", connection);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    purchaseOrders.Add(new PurchaseOrder
                    {
                        po_id = reader.GetInt32(0),
                        supplier_id = reader.GetInt32(1),
                        po_number = reader.GetString(2),
                        order_date = reader.GetDateTime(3),
                        expected_date = reader.GetDateTime(4),
                        status = reader.GetString(5),
                        total_amount = reader.GetDecimal(6),
                        notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                        created_date = reader.GetDateTime(8),
                        modified_date = reader.IsDBNull(9) ? (DateTime?)reader.GetDateTime(9) : null,
                        supplier_name = reader.IsDBNull(10) ? null : reader.GetString(10)
                    });
                }
            }
            catch (SqlException ex)
            {
                if (ex.Message.Contains("Invalid object name 'tbl_purchase_order'"))
                {
                    Console.WriteLine("tbl_purchase_order table doesn't exist yet.");
                    return purchaseOrders;
                }
                throw new Exception($"Database error: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading purchase orders: {ex.Message}");
            }

            return purchaseOrders;
        }

        // Get purchase order by ID
        public async Task<PurchaseOrder> GetPurchaseOrderByIdAsync(int poId)
        {
            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var command = new SqlCommand(@"
                    SELECT po_id, supplier_id, po_number, order_date, expected_date, 
                           status, total_amount, notes, created_date, modified_date
                    FROM tbl_purchase_order
                    WHERE po_id = @po_id", connection);

                command.Parameters.AddWithValue("@po_id", poId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new PurchaseOrder
                    {
                        po_id = reader.GetInt32(0),
                        supplier_id = reader.GetInt32(1),
                        po_number = reader.GetString(2),
                        order_date = reader.GetDateTime(3),
                        expected_date = reader.GetDateTime(4),
                        status = reader.GetString(5),
                        total_amount = reader.GetDecimal(6),
                        notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                        created_date = reader.GetDateTime(8),
                        modified_date = reader.IsDBNull(9) ? (DateTime?)reader.GetDateTime(9) : null
                    };
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading purchase order: {ex.Message}");
            }

            throw new Exception("Purchase order not found");
        }

        // Get purchase order items with product details
        public async Task<List<PurchaseOrderItem>> GetPurchaseOrderItemsAsync(int poId)
        {
            var items = new List<PurchaseOrderItem>();

            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                // Check which primary key column name exists (po_item_id or po_items_id)
                bool hasPoItemId = await ColumnExistsAsync(connection, (SqlTransaction?)null, "tbl_purchase_order_items", "po_item_id");
                bool hasPoItemsId = await ColumnExistsAsync(connection, (SqlTransaction?)null, "tbl_purchase_order_items", "po_items_id");
                // Use po_item_id if it exists, otherwise default to po_items_id
                string pkColumnName = hasPoItemId ? "po_item_id" : (hasPoItemsId ? "po_items_id" : "po_item_id");
                
                // Check if created_date column exists
                bool hasCreatedDate = await ColumnExistsAsync(connection, (SqlTransaction?)null, "tbl_purchase_order_items", "created_date");
                
                string selectColumns = hasCreatedDate
                    ? $"poi.{pkColumnName}, poi.po_id, poi.product_id, poi.quantity_ordered, poi.unit_cost, poi.created_date"
                    : $"poi.{pkColumnName}, poi.po_id, poi.product_id, poi.quantity_ordered, poi.unit_cost";

                var command = new SqlCommand($@"
                    SELECT {selectColumns},
                           p.product_name, p.product_sku
                    FROM tbl_purchase_order_items poi
                    INNER JOIN tbl_product p ON poi.product_id = p.product_id
                    WHERE poi.po_id = @po_id
                    ORDER BY poi.{pkColumnName}", connection);

                command.Parameters.AddWithValue("@po_id", poId);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var item = new PurchaseOrderItem
                    {
                        po_items_id = reader.GetInt32(0),
                        po_id = reader.GetInt32(1),
                        product_id = reader.GetInt32(2),
                        quantity_ordered = reader.GetInt32(3),
                        unit_cost = reader.GetDecimal(4)
                    };

                    // Read created_date if it exists
                    if (hasCreatedDate)
                    {
                        item.created_date = reader.GetDateTime(5);
                        item.product_name = reader.IsDBNull(6) ? null : reader.GetString(6);
                        item.product_sku = reader.IsDBNull(7) ? null : reader.GetString(7);
                    }
                    else
                    {
                        item.created_date = DateTime.Now; // Default value if column doesn't exist
                        item.product_name = reader.IsDBNull(5) ? null : reader.GetString(5);
                        item.product_sku = reader.IsDBNull(6) ? null : reader.GetString(6);
                    }

                    items.Add(item);
                }
            }
            catch (SqlException ex)
            {
                if (ex.Message.Contains("Invalid object name 'tbl_purchase_order_items'"))
                {
                    Console.WriteLine("tbl_purchase_order_items table doesn't exist yet.");
                    return items;
                }
                throw new Exception($"Database error: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading purchase order items: {ex.Message}");
            }

            return items;
        }

        // Update purchase order status
        public async Task<bool> UpdatePurchaseOrderStatusAsync(int poId, string status)
        {
            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var command = new SqlCommand(@"
                    UPDATE tbl_purchase_order 
                    SET status = @status, modified_date = @modified_date
                    WHERE po_id = @po_id", connection);

                command.Parameters.AddWithValue("@po_id", poId);
                command.Parameters.AddWithValue("@status", status);
                command.Parameters.AddWithValue("@modified_date", DateTime.Now);

                return await command.ExecuteNonQueryAsync() > 0;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error updating purchase order status: {ex.Message}");
            }
        }

        // Get all purchase orders (for display in Purchase Order page)
        public async Task<List<PurchaseOrder>> GetAllPurchaseOrdersAsync()
        {
            var purchaseOrders = new List<PurchaseOrder>();

            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var command = new SqlCommand(@"
                    SELECT po.po_id, po.supplier_id, po.po_number, po.order_date, po.expected_date, 
                           po.status, po.total_amount, po.notes, po.created_date, po.modified_date,
                           s.supplier_name
                    FROM tbl_purchase_order po
                    LEFT JOIN tbl_supplier s ON po.supplier_id = s.supplier_id
                    ORDER BY po.order_date DESC", connection);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    purchaseOrders.Add(new PurchaseOrder
                    {
                        po_id = reader.GetInt32(0),
                        supplier_id = reader.GetInt32(1),
                        po_number = reader.GetString(2),
                        order_date = reader.GetDateTime(3),
                        expected_date = reader.GetDateTime(4),
                        status = reader.GetString(5),
                        total_amount = reader.GetDecimal(6),
                        notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                        created_date = reader.GetDateTime(8),
                        modified_date = reader.IsDBNull(9) ? (DateTime?)reader.GetDateTime(9) : null,
                        supplier_name = reader.IsDBNull(10) ? null : reader.GetString(10)
                    });
                }
            }
            catch (SqlException ex)
            {
                if (ex.Message.Contains("Invalid object name 'tbl_purchase_order'"))
                {
                    Console.WriteLine("tbl_purchase_order table doesn't exist yet.");
                    return purchaseOrders;
                }
                throw new Exception($"Database error: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading purchase orders: {ex.Message}");
            }

            return purchaseOrders;
        }

        // Create purchase order with items
        public async Task<int> CreatePurchaseOrderAsync(int supplierId, DateTime orderDate, DateTime expectedDate, string? notes, List<PurchaseOrderItem> items)
        {
            using var connection = db.GetConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Generate PO Number
                string poNumber = await GeneratePONumberAsync(connection, transaction);

                // Calculate total amount
                decimal totalAmount = items.Sum(item => item.quantity_ordered * item.unit_cost);

                // Step 1: Insert Purchase Order header
                var insertPOCommand = new SqlCommand(@"
                    INSERT INTO tbl_purchase_order (supplier_id, po_number, order_date, expected_date, status, total_amount, notes, created_date)
                    VALUES (@supplier_id, @po_number, @order_date, @expected_date, @status, @total_amount, @notes, @created_date);
                    SELECT SCOPE_IDENTITY();", connection, transaction);

                insertPOCommand.Parameters.AddWithValue("@supplier_id", supplierId);
                insertPOCommand.Parameters.AddWithValue("@po_number", poNumber);
                insertPOCommand.Parameters.AddWithValue("@order_date", orderDate);
                insertPOCommand.Parameters.AddWithValue("@expected_date", expectedDate);
                insertPOCommand.Parameters.AddWithValue("@status", "Pending");
                insertPOCommand.Parameters.AddWithValue("@total_amount", totalAmount);
                insertPOCommand.Parameters.AddWithValue("@notes", (object)notes ?? DBNull.Value);
                insertPOCommand.Parameters.AddWithValue("@created_date", DateTime.Now);

                var poId = Convert.ToInt32(await insertPOCommand.ExecuteScalarAsync());

                // Step 2: Insert Purchase Order items
                foreach (var item in items)
                {
                    // Build dynamic INSERT based on column existence
                    var insertColumns = new List<string> { "po_id", "product_id", "quantity_ordered", "unit_cost" };
                    var insertValues = new List<string> { "@po_id", "@product_id", "@quantity_ordered", "@unit_cost" };
                    
                    // Check if created_date column exists and add it if it does
                    bool hasCreatedDate = await ColumnExistsAsync(connection, transaction, "tbl_purchase_order_items", "created_date");
                    if (hasCreatedDate)
                    {
                        insertColumns.Add("created_date");
                        insertValues.Add("@created_date");
                    }

                    var insertSql = $@"
                        INSERT INTO tbl_purchase_order_items ({string.Join(", ", insertColumns)})
                        VALUES ({string.Join(", ", insertValues)})";

                    var insertItemCommand = new SqlCommand(insertSql, connection, transaction);

                    insertItemCommand.Parameters.AddWithValue("@po_id", poId);
                    insertItemCommand.Parameters.AddWithValue("@product_id", item.product_id);
                    insertItemCommand.Parameters.AddWithValue("@quantity_ordered", item.quantity_ordered);
                    insertItemCommand.Parameters.AddWithValue("@unit_cost", item.unit_cost);
                    
                    if (hasCreatedDate)
                    {
                        insertItemCommand.Parameters.AddWithValue("@created_date", DateTime.Now);
                    }

                    await insertItemCommand.ExecuteNonQueryAsync();
                }

                // Commit transaction
                transaction.Commit();

                return poId;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                throw new Exception($"Error creating purchase order: {ex.Message}");
            }
        }

        // Generate PO Number (PO-001, PO-002, etc.)
        private async Task<string> GeneratePONumberAsync(SqlConnection connection, SqlTransaction transaction)
        {
            var command = new SqlCommand(@"
                SELECT COUNT(*) FROM tbl_purchase_order", connection, transaction);
            
            var count = (int)await command.ExecuteScalarAsync();
            return $"PO-{(count + 1).ToString("D3")}";
        }

        // Helper method to check if a column exists in a table
        private async Task<bool> ColumnExistsAsync(SqlConnection connection, SqlTransaction? transaction, string tableName, string columnName)
        {
            try
            {
                var command = new SqlCommand(@"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = @table_name AND COLUMN_NAME = @column_name", connection, transaction);
                
                command.Parameters.AddWithValue("@table_name", tableName);
                command.Parameters.AddWithValue("@column_name", columnName);
                
                var count = (int)await command.ExecuteScalarAsync();
                return count > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}

