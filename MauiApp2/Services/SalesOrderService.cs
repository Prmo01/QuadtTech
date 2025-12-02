using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MauiApp2.Models;
using MauiApp2.Components.Database;

namespace MauiApp2.Services
{
    public interface ISalesOrderService
    {
        Task<int> CreateSalesOrderAsync(DateTime salesDate, string paymentMethod, List<SalesOrderItem> items, int userId);
        Task<List<SalesOrder>> GetAllSalesOrdersAsync();
        Task<SalesOrder> GetSalesOrderByIdAsync(int salesOrderId);
        Task<List<SalesOrderItem>> GetSalesOrderItemsAsync(int salesOrderId);
    }

    public class SalesOrderService : ISalesOrderService
    {
        private readonly IStockOutService _stockOutService;

        public SalesOrderService(IStockOutService stockOutService)
        {
            _stockOutService = stockOutService;
        }

        // Create Sales Order and automatically create Stock Out
        public async Task<int> CreateSalesOrderAsync(DateTime salesDate, string paymentMethod, List<SalesOrderItem> items, int userId)
        {
            using var connection = db.GetConnection();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Step 1: Generate Sales Order Number (INV-001, INV-002, etc.)
                string salesOrderNumber = await GenerateSalesOrderNumberAsync(connection, transaction);

                // Step 2: Calculate totals and get tax rates for each item
                decimal subtotal = 0;
                decimal totalTax = 0;
                decimal totalAmount = 0;

                foreach (var item in items)
                {
                    // Get product details including tax rate
                    var product = await GetProductWithTaxAsync(connection, transaction, item.product_id);
                    if (product == null)
                    {
                        throw new Exception($"Product with ID {item.product_id} not found");
                    }

                    // Check stock availability
                    if (product.quantity < item.quantity)
                    {
                        throw new Exception($"Insufficient stock for product {product.product_name}. Available: {product.quantity}, Requested: {item.quantity}");
                    }

                    // Set unit price from product or use provided price
                    if (item.unit_price <= 0)
                    {
                        item.unit_price = product.sell_price;
                    }

                    // Get tax rate (default to 0 if no tax)
                    decimal taxRate = 0;
                    if (product.tax_id.HasValue)
                    {
                        taxRate = await GetTaxRateAsync(connection, transaction, product.tax_id.Value);
                    }

                    item.tax_rate = taxRate;

                    // Calculate item totals
                    item.subtotal = item.quantity * item.unit_price;
                    item.tax_amount = item.subtotal * taxRate;
                    item.total = item.subtotal + item.tax_amount;

                    subtotal += item.subtotal;
                    totalTax += item.tax_amount;
                    totalAmount += item.total;
                }

                // Step 3: Create Sales Order header
                var salesOrderId = await CreateSalesOrderHeaderAsync(connection, transaction, salesOrderNumber, salesDate, subtotal, totalTax, totalAmount, paymentMethod, userId);

                // Step 4: Create Sales Order items and reduce inventory
                foreach (var item in items)
                {
                    await CreateSalesOrderItemAsync(connection, transaction, salesOrderId, item);
                    
                    // Reduce product inventory (quantity decreases)
                    await ReduceProductInventoryAsync(connection, transaction, item.product_id, item.quantity);
                }

                // Step 5: Automatically create Stock Out (within same transaction)
                var stockOutItems = items.ConvertAll(item => new StockOutItem
                {
                    product_id = item.product_id,
                    quantity = item.quantity,
                    reason = "Sale"
                });

                await _stockOutService.CreateStockOutFromSaleAsync(connection, transaction, salesOrderId, stockOutItems, userId);

                // Commit transaction
                transaction.Commit();

                return salesOrderId;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                throw new Exception($"Error creating sales order: {ex.Message}");
            }
        }

        // Generate Sales Order Number (INV-001, INV-002, etc.)
        private async Task<string> GenerateSalesOrderNumberAsync(SqlConnection connection, SqlTransaction transaction)
        {
            var command = new SqlCommand(@"
                SELECT COUNT(*) FROM tbl_sales_order", connection, transaction);
            
            var count = (int)await command.ExecuteScalarAsync();
            return $"INV-{(count + 1).ToString("D3")}";
        }

        // Get product with tax info
        private async Task<Product> GetProductWithTaxAsync(SqlConnection connection, SqlTransaction transaction, int productId)
        {
            var command = new SqlCommand(@"
                SELECT product_id, brand_id, category_id, tax_id, product_name, product_sku, 
                       model_number, cost_price, sell_price, quantity, status, created_date, modified_date
                FROM tbl_product
                WHERE product_id = @product_id", connection, transaction);

            command.Parameters.AddWithValue("@product_id", productId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Product
                {
                    product_id = reader.GetInt32(0),
                    brand_id = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    category_id = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    tax_id = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    product_name = reader.GetString(4),
                    product_sku = reader.GetString(5),
                    model_number = reader.IsDBNull(6) ? null : reader.GetString(6),
                    cost_price = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                    sell_price = reader.GetDecimal(8),
                    quantity = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    status = reader.IsDBNull(10) ? null : reader.GetBoolean(10),
                    created_date = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                    modified_date = reader.IsDBNull(12) ? null : reader.GetDateTime(12)
                };
            }
            return null;
        }

        // Get tax rate from tax_id
        private async Task<decimal> GetTaxRateAsync(SqlConnection connection, SqlTransaction transaction, int taxId)
        {
            var command = new SqlCommand(@"
                SELECT tax_rate FROM tbl_tax WHERE tax_id = @tax_id AND is_active = 1", connection, transaction);

            command.Parameters.AddWithValue("@tax_id", taxId);

            var result = await command.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
            {
                return Convert.ToDecimal(result);
            }
            return 0;
        }

        // Create Sales Order header
        private async Task<int> CreateSalesOrderHeaderAsync(SqlConnection connection, SqlTransaction transaction, string salesOrderNumber, DateTime salesDate, decimal subtotal, decimal taxAmount, decimal totalAmount, string paymentMethod, int userId)
        {
            var command = new SqlCommand(@"
                INSERT INTO tbl_sales_order (sales_order_number, sales_date, subtotal, tax_amount, total_amount, payment_method, processed_by, created_date)
                VALUES (@sales_order_number, @sales_date, @subtotal, @tax_amount, @total_amount, @payment_method, @processed_by, @created_date);
                SELECT SCOPE_IDENTITY();", connection, transaction);

            command.Parameters.AddWithValue("@sales_order_number", salesOrderNumber);
            command.Parameters.AddWithValue("@sales_date", salesDate);
            command.Parameters.AddWithValue("@subtotal", subtotal);
            command.Parameters.AddWithValue("@tax_amount", taxAmount);
            command.Parameters.AddWithValue("@total_amount", totalAmount);
            command.Parameters.AddWithValue("@payment_method", paymentMethod);
            command.Parameters.AddWithValue("@processed_by", userId);
            command.Parameters.AddWithValue("@created_date", DateTime.Now);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        // Create Sales Order item
        private async Task CreateSalesOrderItemAsync(SqlConnection connection, SqlTransaction transaction, int salesOrderId, SalesOrderItem item)
        {
            var command = new SqlCommand(@"
                INSERT INTO tbl_sales_order_items (sales_order_id, product_id, quantity, unit_price, tax_rate, tax_amount, subtotal, total)
                VALUES (@sales_order_id, @product_id, @quantity, @unit_price, @tax_rate, @tax_amount, @subtotal, @total)", connection, transaction);

            command.Parameters.AddWithValue("@sales_order_id", salesOrderId);
            command.Parameters.AddWithValue("@product_id", item.product_id);
            command.Parameters.AddWithValue("@quantity", item.quantity);
            command.Parameters.AddWithValue("@unit_price", item.unit_price);
            command.Parameters.AddWithValue("@tax_rate", item.tax_rate);
            command.Parameters.AddWithValue("@tax_amount", item.tax_amount);
            command.Parameters.AddWithValue("@subtotal", item.subtotal);
            command.Parameters.AddWithValue("@total", item.total);

            await command.ExecuteNonQueryAsync();
        }

        // Reduce product inventory (quantity decreases)
        private async Task ReduceProductInventoryAsync(SqlConnection connection, SqlTransaction transaction, int productId, int quantity)
        {
            var command = new SqlCommand(@"
                UPDATE tbl_product 
                SET quantity = ISNULL(quantity, 0) - @quantity,
                    modified_date = @modified_date
                WHERE product_id = @product_id", connection, transaction);

            command.Parameters.AddWithValue("@product_id", productId);
            command.Parameters.AddWithValue("@quantity", quantity);
            command.Parameters.AddWithValue("@modified_date", DateTime.Now);

            await command.ExecuteNonQueryAsync();
        }

        // Get all sales orders
        public async Task<List<SalesOrder>> GetAllSalesOrdersAsync()
        {
            var salesOrders = new List<SalesOrder>();

            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var command = new SqlCommand(@"
                    SELECT so.sales_order_id, so.sales_order_number, so.sales_date, so.subtotal, so.tax_amount, 
                           so.total_amount, so.payment_method, so.processed_by, so.created_date,
                           u.full_name,
                           (SELECT COUNT(*) FROM tbl_sales_order_items WHERE sales_order_id = so.sales_order_id) as item_count
                    FROM tbl_sales_order so
                    LEFT JOIN tbl_users u ON so.processed_by = u.user_id
                    ORDER BY so.sales_date DESC", connection);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    salesOrders.Add(new SalesOrder
                    {
                        sales_order_id = reader.GetInt32(0),
                        sales_order_number = reader.GetString(1),
                        sales_date = reader.GetDateTime(2),
                        subtotal = reader.GetDecimal(3),
                        tax_amount = reader.GetDecimal(4),
                        total_amount = reader.GetDecimal(5),
                        payment_method = reader.GetString(6),
                        processed_by = reader.GetInt32(7),
                        created_date = reader.GetDateTime(8),
                        processed_by_name = reader.IsDBNull(9) ? null : reader.GetString(9),
                        item_count = reader.IsDBNull(10) ? null : reader.GetInt32(10)
                    });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading sales orders: {ex.Message}");
            }

            return salesOrders;
        }

        // Get sales order by ID
        public async Task<SalesOrder> GetSalesOrderByIdAsync(int salesOrderId)
        {
            using var connection = db.GetConnection();
            await connection.OpenAsync();

            var command = new SqlCommand(@"
                SELECT so.sales_order_id, so.sales_order_number, so.sales_date, so.subtotal, so.tax_amount, 
                       so.total_amount, so.payment_method, so.processed_by, so.created_date,
                       u.full_name,
                       (SELECT COUNT(*) FROM tbl_sales_order_items WHERE sales_order_id = so.sales_order_id) as item_count
                FROM tbl_sales_order so
                LEFT JOIN tbl_users u ON so.processed_by = u.user_id
                WHERE so.sales_order_id = @sales_order_id", connection);

            command.Parameters.AddWithValue("@sales_order_id", salesOrderId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new SalesOrder
                {
                    sales_order_id = reader.GetInt32(0),
                    sales_order_number = reader.GetString(1),
                    sales_date = reader.GetDateTime(2),
                    subtotal = reader.GetDecimal(3),
                    tax_amount = reader.GetDecimal(4),
                    total_amount = reader.GetDecimal(5),
                    payment_method = reader.GetString(6),
                    processed_by = reader.GetInt32(7),
                    created_date = reader.GetDateTime(8),
                    processed_by_name = reader.IsDBNull(9) ? null : reader.GetString(9),
                    item_count = reader.IsDBNull(10) ? null : reader.GetInt32(10)
                };
            }

            throw new Exception("Sales order not found");
        }

        // Get sales order items
        public async Task<List<SalesOrderItem>> GetSalesOrderItemsAsync(int salesOrderId)
        {
            var items = new List<SalesOrderItem>();

            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var command = new SqlCommand(@"
                    SELECT soi.sales_order_item_id, soi.sales_order_id, soi.product_id, soi.quantity, 
                           soi.unit_price, soi.tax_rate, soi.tax_amount, soi.subtotal, soi.total,
                           p.product_name, p.product_sku
                    FROM tbl_sales_order_items soi
                    LEFT JOIN tbl_product p ON soi.product_id = p.product_id
                    WHERE soi.sales_order_id = @sales_order_id
                    ORDER BY soi.sales_order_item_id", connection);

                command.Parameters.AddWithValue("@sales_order_id", salesOrderId);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    items.Add(new SalesOrderItem
                    {
                        sales_order_item_id = reader.GetInt32(0),
                        sales_order_id = reader.GetInt32(1),
                        product_id = reader.GetInt32(2),
                        quantity = reader.GetInt32(3),
                        unit_price = reader.GetDecimal(4),
                        tax_rate = reader.GetDecimal(5),
                        tax_amount = reader.GetDecimal(6),
                        subtotal = reader.GetDecimal(7),
                        total = reader.GetDecimal(8),
                        product_name = reader.IsDBNull(9) ? null : reader.GetString(9),
                        product_sku = reader.IsDBNull(10) ? null : reader.GetString(10)
                    });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading sales order items: {ex.Message}");
            }

            return items;
        }
    }
}


