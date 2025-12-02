using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MauiApp2.Models;
using MauiApp2.Components.Database;

namespace MauiApp2.Services
{
    public interface IStockInService
    {
        Task<int> ReceiveStockFromPurchaseOrderAsync(int poId, List<StockInItem> items, string? notes, int userId);
        Task<List<StockIn>> GetStockInHistoryAsync();
        Task<StockIn> GetStockInByIdAsync(int stockInId);
    }

    public class StockInService : IStockInService
    {
        public StockInService()
        {
        }

        // Receive stock from Purchase Order - simple, one method does everything
        public async Task<int> ReceiveStockFromPurchaseOrderAsync(int poId, List<StockInItem> items, string? notes, int userId)
        {
            using var connection = db.GetConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Step 1: Get Purchase Order details within transaction
                var getPOCommand = new SqlCommand(@"
                    SELECT supplier_id FROM tbl_purchase_order WHERE po_id = @po_id", connection, transaction);
                getPOCommand.Parameters.AddWithValue("@po_id", poId);
                
                var supplierIdResult = await getPOCommand.ExecuteScalarAsync();
                if (supplierIdResult == null || supplierIdResult == DBNull.Value)
                {
                    throw new Exception("Purchase order not found");
                }
                int supplierId = Convert.ToInt32(supplierIdResult);

                // Step 2: Generate Stock In Number
                string stockInNumber = await GenerateStockInNumberAsync(connection, transaction);

                // Step 3: Create Stock In header
                var stockInId = await CreateStockInHeaderAsync(connection, transaction, poId, supplierId, stockInNumber, notes, userId);

                // Step 4: Create Stock In items and update inventory
                foreach (var item in items)
                {
                    // Insert stock in item
                    await CreateStockInItemAsync(connection, transaction, stockInId, item);

                    // Update product inventory (quantity increases)
                    await UpdateProductInventoryAsync(connection, transaction, item.product_id, item.quantity_received, item.unit_cost);
                }

                // Step 5: Update PO status to "Received"
                var updatePOCommand = new SqlCommand(@"
                    UPDATE tbl_purchase_order 
                    SET status = @status, modified_date = @modified_date
                    WHERE po_id = @po_id", connection, transaction);
                updatePOCommand.Parameters.AddWithValue("@po_id", poId);
                updatePOCommand.Parameters.AddWithValue("@status", "Received");
                updatePOCommand.Parameters.AddWithValue("@modified_date", DateTime.Now);
                await updatePOCommand.ExecuteNonQueryAsync();

                // Commit transaction
                transaction.Commit();

                return stockInId;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                throw new Exception($"Error receiving stock: {ex.Message}");
            }
        }

        // Generate Stock In Number (SI-001, SI-002, etc.)
        private async Task<string> GenerateStockInNumberAsync(SqlConnection connection, SqlTransaction transaction)
        {
            var command = new SqlCommand(@"
                SELECT COUNT(*) FROM tbl_stock_in", connection, transaction);
            
            var count = (int)await command.ExecuteScalarAsync();
            return $"SI-{(count + 1).ToString("D3")}";
        }

        // Create Stock In header
        private async Task<int> CreateStockInHeaderAsync(SqlConnection connection, SqlTransaction transaction, int poId, int supplierId, string stockInNumber, string? notes, int userId)
        {
            var command = new SqlCommand(@"
                INSERT INTO tbl_stock_in (po_id, supplier_id, stock_in_number, stock_in_date, notes, processed_by, created_date)
                VALUES (@po_id, @supplier_id, @stock_in_number, @stock_in_date, @notes, @processed_by, @created_date);
                SELECT SCOPE_IDENTITY();", connection, transaction);

            command.Parameters.AddWithValue("@po_id", poId);
            command.Parameters.AddWithValue("@supplier_id", supplierId);
            command.Parameters.AddWithValue("@stock_in_number", stockInNumber);
            command.Parameters.AddWithValue("@stock_in_date", DateTime.Now);
            command.Parameters.AddWithValue("@notes", (object)notes ?? DBNull.Value);
            command.Parameters.AddWithValue("@processed_by", userId);
            command.Parameters.AddWithValue("@created_date", DateTime.Now);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        // Create Stock In item
        private async Task CreateStockInItemAsync(SqlConnection connection, SqlTransaction transaction, int stockInId, StockInItem item)
        {
            // Build dynamic INSERT based on column existence
            var insertColumns = new List<string> { "stock_in_id", "product_id", "quantity_received", "unit_cost", "created_date" };
            var insertValues = new List<string> { "@stock_in_id", "@product_id", "@quantity_received", "@unit_cost", "@created_date" };
            
            // Check if rejected columns exist and add them if they do
            bool hasRejectedColumns = await ColumnExistsAsync(connection, transaction, "tbl_stock_in_items", "quantity_rejected");
            if (hasRejectedColumns)
            {
                insertColumns.Add("quantity_rejected");
                insertValues.Add("@quantity_rejected");
            }
            
            bool hasRejectionReason = await ColumnExistsAsync(connection, transaction, "tbl_stock_in_items", "rejection_reason");
            if (hasRejectionReason)
            {
                insertColumns.Add("rejection_reason");
                insertValues.Add("@rejection_reason");
            }
            
            bool hasRejectionRemarks = await ColumnExistsAsync(connection, transaction, "tbl_stock_in_items", "rejection_remarks");
            if (hasRejectionRemarks)
            {
                insertColumns.Add("rejection_remarks");
                insertValues.Add("@rejection_remarks");
            }

            var insertSql = $@"
                INSERT INTO tbl_stock_in_items ({string.Join(", ", insertColumns)})
                VALUES ({string.Join(", ", insertValues)})";

            var command = new SqlCommand(insertSql, connection, transaction);

            command.Parameters.AddWithValue("@stock_in_id", stockInId);
            command.Parameters.AddWithValue("@product_id", item.product_id);
            command.Parameters.AddWithValue("@quantity_received", item.quantity_received);
            command.Parameters.AddWithValue("@unit_cost", item.unit_cost);
            command.Parameters.AddWithValue("@created_date", DateTime.Now);
            
            if (hasRejectedColumns)
            {
                command.Parameters.AddWithValue("@quantity_rejected", item.quantity_rejected);
            }
            
            if (hasRejectionReason)
            {
                command.Parameters.AddWithValue("@rejection_reason", (object)item.rejection_reason ?? DBNull.Value);
            }
            
            if (hasRejectionRemarks)
            {
                command.Parameters.AddWithValue("@rejection_remarks", (object)item.rejection_remarks ?? DBNull.Value);
            }

            await command.ExecuteNonQueryAsync();
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

        // Update product inventory (increase quantity and update cost price)
        private async Task UpdateProductInventoryAsync(SqlConnection connection, SqlTransaction transaction, int productId, int quantityReceived, decimal unitCost)
        {
            // Update quantity (increase)
            var updateQuantityCommand = new SqlCommand(@"
                UPDATE tbl_product 
                SET quantity = ISNULL(quantity, 0) + @quantity_received,
                    cost_price = @unit_cost,
                    modified_date = @modified_date
                WHERE product_id = @product_id", connection, transaction);

            updateQuantityCommand.Parameters.AddWithValue("@product_id", productId);
            updateQuantityCommand.Parameters.AddWithValue("@quantity_received", quantityReceived);
            updateQuantityCommand.Parameters.AddWithValue("@unit_cost", unitCost);
            updateQuantityCommand.Parameters.AddWithValue("@modified_date", DateTime.Now);

            await updateQuantityCommand.ExecuteNonQueryAsync();
        }

        // Get Stock In history
        public async Task<List<StockIn>> GetStockInHistoryAsync()
        {
            var stockIns = new List<StockIn>();

            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var command = new SqlCommand(@"
                    SELECT si.stock_in_id, si.po_id, si.supplier_id, si.stock_in_number, si.stock_in_date, 
                           si.notes, si.processed_by, si.created_date,
                           s.supplier_name, po.po_number, u.full_name
                    FROM tbl_stock_in si
                    LEFT JOIN tbl_supplier s ON si.supplier_id = s.supplier_id
                    LEFT JOIN tbl_purchase_order po ON si.po_id = po.po_id
                    LEFT JOIN tbl_users u ON si.processed_by = u.user_id
                    ORDER BY si.stock_in_date DESC", connection);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    stockIns.Add(new StockIn
                    {
                        stock_in_id = reader.GetInt32(0),
                        po_id = reader.GetInt32(1),
                        supplier_id = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                        stock_in_number = reader.GetString(3),
                        stock_in_date = reader.GetDateTime(4),
                        notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                        processed_by = reader.GetInt32(6),
                        created_date = reader.GetDateTime(7),
                        supplier_name = reader.IsDBNull(8) ? null : reader.GetString(8),
                        po_number = reader.IsDBNull(9) ? null : reader.GetString(9),
                        processed_by_name = reader.IsDBNull(10) ? null : reader.GetString(10)
                    });
                }
            }
            catch (SqlException ex)
            {
                if (ex.Message.Contains("Invalid object name 'tbl_stock_in'"))
                {
                    Console.WriteLine("tbl_stock_in table doesn't exist yet.");
                    return stockIns;
                }
                throw new Exception($"Database error: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading stock in history: {ex.Message}");
            }

            return stockIns;
        }

        // Get Stock In by ID
        public async Task<StockIn> GetStockInByIdAsync(int stockInId)
        {
            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var command = new SqlCommand(@"
                    SELECT si.stock_in_id, si.po_id, si.supplier_id, si.stock_in_number, si.stock_in_date, 
                           si.notes, si.processed_by, si.created_date,
                           s.supplier_name, po.po_number, u.full_name
                    FROM tbl_stock_in si
                    LEFT JOIN tbl_supplier s ON si.supplier_id = s.supplier_id
                    LEFT JOIN tbl_purchase_order po ON si.po_id = po.po_id
                    LEFT JOIN tbl_users u ON si.processed_by = u.user_id
                    WHERE si.stock_in_id = @stock_in_id", connection);

                command.Parameters.AddWithValue("@stock_in_id", stockInId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new StockIn
                    {
                        stock_in_id = reader.GetInt32(0),
                        po_id = reader.GetInt32(1),
                        supplier_id = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                        stock_in_number = reader.GetString(3),
                        stock_in_date = reader.GetDateTime(4),
                        notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                        processed_by = reader.GetInt32(6),
                        created_date = reader.GetDateTime(7),
                        supplier_name = reader.IsDBNull(8) ? null : reader.GetString(8),
                        po_number = reader.IsDBNull(9) ? null : reader.GetString(9),
                        processed_by_name = reader.IsDBNull(10) ? null : reader.GetString(10)
                    };
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading stock in: {ex.Message}");
            }

            throw new Exception("Stock In not found");
        }
    }
}

