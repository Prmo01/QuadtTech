using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MauiApp2.Models;
using MauiApp2.Components.Database;

namespace MauiApp2.Services
{
    public interface IProductService
    {
        Task<List<Product>> GetProductsAsync();
        Task<int> CreateProductAsync(Product product);
        Task<bool> UpdateProductAsync(Product product);
        Task<bool> DeleteProductAsync(int productId);
        Task<Product> GetProductByIdAsync(int productId);
        Task<Product> GetProductBySkuAsync(string sku);
    }

    public class ProductService : IProductService
    {
        // READ - Get all products
        public async Task<List<Product>> GetProductsAsync()
        {
            var products = new List<Product>();

            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var command = new SqlCommand(@"
                    SELECT product_id, brand_id, category_id, product_name, product_sku, 
                           model_number, cost_price, sell_price, quantity, status, 
                           created_date, modified_date, tax_id, is_tax_inclusive 
                    FROM tbl_product 
                    ORDER BY product_name", connection);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    products.Add(new Product
                    {
                        product_id = reader.GetInt32(0),
                        brand_id = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                        category_id = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                        product_name = reader.GetString(3),
                        product_sku = reader.GetString(4),
                        model_number = reader.IsDBNull(5) ? null : reader.GetString(5),
                        cost_price = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                        sell_price = reader.GetDecimal(7),
                        quantity = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        status = reader.IsDBNull(9) ? null : reader.GetBoolean(9),
                        created_date = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                        modified_date = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                        tax_id = reader.IsDBNull(12) ? 1 : reader.GetInt32(12),
                        is_tax_inclusive = reader.IsDBNull(13) ? true : reader.GetBoolean(13)
                    });
                }
            }
            catch (SqlException ex)
            {
                if (ex.Message.Contains("Invalid object name 'tbl_product'"))
                {
                    Console.WriteLine("tbl_product table doesn't exist yet.");
                    return products;
                }
                throw new Exception($"Database error: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading products: {ex.Message}");
            }

            return products;
        }

        // Generate unique SKU in format: brand-category-number
        private async Task<string> GenerateSkuAsync(SqlConnection connection, int? brandId, int? categoryId)
        {
            // Get brand name
            string brandName = "GEN"; // Default if no brand
            if (brandId.HasValue)
            {
                var brandCommand = new SqlCommand(
                    "SELECT brand_name FROM tbl_brand WHERE brand_id = @brand_id",
                    connection);
                brandCommand.Parameters.AddWithValue("@brand_id", brandId.Value);
                var brandResult = await brandCommand.ExecuteScalarAsync();
                if (brandResult != null && !DBNull.Value.Equals(brandResult))
                {
                    brandName = brandResult.ToString() ?? "GEN";
                }
            }

            // Get category name
            string categoryName = "GEN"; // Default if no category
            if (categoryId.HasValue)
            {
                var categoryCommand = new SqlCommand(
                    "SELECT category_name FROM tbl_category WHERE category_id = @category_id",
                    connection);
                categoryCommand.Parameters.AddWithValue("@category_id", categoryId.Value);
                var categoryResult = await categoryCommand.ExecuteScalarAsync();
                if (categoryResult != null && !DBNull.Value.Equals(categoryResult))
                {
                    categoryName = categoryResult.ToString() ?? "GEN";
                }
            }

            // Sanitize names: remove spaces, special characters, convert to uppercase
            brandName = SanitizeForSku(brandName);
            categoryName = SanitizeForSku(categoryName);

            string sku;
            bool exists;
            int counter = 1;

            do
            {
                // Generate SKU format: brand-category-number
                string counterSuffix = counter.ToString("D4");
                sku = $"{brandName}-{categoryName}-{counterSuffix}";

                // Check if SKU already exists
                var checkCommand = new SqlCommand(
                    "SELECT COUNT(*) FROM tbl_product WHERE product_sku = @product_sku",
                    connection);
                checkCommand.Parameters.AddWithValue("@product_sku", sku);

                var count = (int)await checkCommand.ExecuteScalarAsync();
                exists = count > 0;

                if (exists)
                {
                    counter++;
                    checkCommand.Parameters.Clear();
                }
            } while (exists);

            return sku;
        }

        // Sanitize string for SKU: remove spaces, special chars, uppercase
        private string SanitizeForSku(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "GEN";

            // Remove all non-alphanumeric characters except dashes, convert to uppercase
            var sanitized = Regex.Replace(input.ToUpper(), @"[^A-Z0-9-]", "");

            // Remove multiple consecutive dashes
            sanitized = Regex.Replace(sanitized, @"-+", "-");

            // Remove leading/trailing dashes
            sanitized = sanitized.Trim('-');

            // Limit length to 10 characters for brand/category
            if (sanitized.Length > 10)
                sanitized = sanitized.Substring(0, 10);

            return string.IsNullOrWhiteSpace(sanitized) ? "GEN" : sanitized;
        }

        // CREATE - Add new product
        public async Task<int> CreateProductAsync(Product product)
        {
            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                // Auto-generate SKU if not provided or empty
                string sku = product.product_sku;
                if (string.IsNullOrWhiteSpace(sku))
                {
                    sku = await GenerateSkuAsync(connection, product.brand_id, product.category_id);
                }
                else
                {
                    // Check if provided SKU already exists
                    var checkCommand = new SqlCommand(
                        "SELECT COUNT(*) FROM tbl_product WHERE product_sku = @product_sku",
                        connection);
                    checkCommand.Parameters.AddWithValue("@product_sku", sku);
                    var count = (int)await checkCommand.ExecuteScalarAsync();

                    if (count > 0)
                    {
                        throw new Exception($"A product with SKU '{sku}' already exists.");
                    }
                }

                var command = new SqlCommand(@"
                    INSERT INTO tbl_product (brand_id, category_id, product_name, product_sku, 
                                             model_number, cost_price, sell_price, quantity, 
                                             status, created_date, modified_date, tax_id, is_tax_inclusive)
                    VALUES (@brand_id, @category_id, @product_name, @product_sku, 
                            @model_number, @cost_price, @sell_price, @quantity, 
                            @status, @created_date, @modified_date, @tax_id, @is_tax_inclusive);
                    SELECT SCOPE_IDENTITY();", connection);

                command.Parameters.AddWithValue("@brand_id", (object)product.brand_id ?? DBNull.Value);
                command.Parameters.AddWithValue("@category_id", (object)product.category_id ?? DBNull.Value);
                command.Parameters.AddWithValue("@product_name", product.product_name);
                command.Parameters.AddWithValue("@product_sku", sku);
                command.Parameters.AddWithValue("@model_number", (object)product.model_number ?? DBNull.Value);
                command.Parameters.AddWithValue("@cost_price", (object)product.cost_price ?? DBNull.Value);
                command.Parameters.AddWithValue("@sell_price", product.sell_price);
                command.Parameters.AddWithValue("@quantity", (object)product.quantity ?? DBNull.Value);
                command.Parameters.AddWithValue("@status", (object)product.status ?? DBNull.Value);
                command.Parameters.AddWithValue("@created_date", product.created_date ?? DateTime.Now);
                command.Parameters.AddWithValue("@modified_date", product.modified_date ?? DateTime.Now);
                command.Parameters.AddWithValue("@tax_id", (object)product.tax_id ?? 1);
                command.Parameters.AddWithValue("@is_tax_inclusive", product.is_tax_inclusive);

                var result = await command.ExecuteScalarAsync();

                // Update the product object with the generated SKU
                product.product_sku = sku;

                return Convert.ToInt32(result);
            }
            catch (SqlException ex)
            {
                if (ex.Message.Contains("Invalid object name 'tbl_product'"))
                {
                    throw new Exception("tbl_product table doesn't exist. Please create the table first.");
                }
                if (ex.Message.Contains("UNIQUE KEY constraint") || ex.Message.Contains("duplicate key"))
                {
                    throw new Exception($"A product with SKU '{product.product_sku}' already exists.");
                }
                throw new Exception($"Error creating product: {ex.Message}");
            }
        }

        // UPDATE - Modify existing product
        public async Task<bool> UpdateProductAsync(Product product)
        {
            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var command = new SqlCommand(@"
                    UPDATE tbl_product 
                    SET brand_id = @brand_id, 
                        category_id = @category_id, 
                        product_name = @product_name, 
                        product_sku = @product_sku, 
                        model_number = @model_number, 
                        cost_price = @cost_price, 
                        sell_price = @sell_price, 
                        quantity = @quantity, 
                        status = @status, 
                        modified_date = @modified_date,
                        tax_id = @tax_id,
                        is_tax_inclusive = @is_tax_inclusive
                    WHERE product_id = @product_id", connection);

                command.Parameters.AddWithValue("@product_id", product.product_id);
                command.Parameters.AddWithValue("@brand_id", (object)product.brand_id ?? DBNull.Value);
                command.Parameters.AddWithValue("@category_id", (object)product.category_id ?? DBNull.Value);
                command.Parameters.AddWithValue("@product_name", product.product_name);
                command.Parameters.AddWithValue("@product_sku", product.product_sku);
                command.Parameters.AddWithValue("@model_number", (object)product.model_number ?? DBNull.Value);
                command.Parameters.AddWithValue("@cost_price", (object)product.cost_price ?? DBNull.Value);
                command.Parameters.AddWithValue("@sell_price", product.sell_price);
                command.Parameters.AddWithValue("@quantity", (object)product.quantity ?? DBNull.Value);
                command.Parameters.AddWithValue("@status", (object)product.status ?? DBNull.Value);
                command.Parameters.AddWithValue("@modified_date", DateTime.Now);
                command.Parameters.AddWithValue("@tax_id", (object)product.tax_id ?? 1);
                command.Parameters.AddWithValue("@is_tax_inclusive", product.is_tax_inclusive);

                return await command.ExecuteNonQueryAsync() > 0;
            }
            catch (SqlException ex)
            {
                if (ex.Message.Contains("UNIQUE KEY constraint") || ex.Message.Contains("duplicate key"))
                {
                    throw new Exception($"A product with SKU '{product.product_sku}' already exists.");
                }
                throw new Exception($"Error updating product: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error updating product: {ex.Message}");
            }
        }

        // DELETE - Remove product
        public async Task<bool> DeleteProductAsync(int productId)
        {
            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var deleteCommand = new SqlCommand("DELETE FROM tbl_product WHERE product_id = @product_id", connection);
                deleteCommand.Parameters.AddWithValue("@product_id", productId);

                return await deleteCommand.ExecuteNonQueryAsync() > 0;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error deleting product: {ex.Message}");
            }
        }

        // READ - Get product by ID
        public async Task<Product> GetProductByIdAsync(int productId)
        {
            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var command = new SqlCommand(@"
                    SELECT product_id, brand_id, category_id, product_name, product_sku, 
                           model_number, cost_price, sell_price, quantity, status, 
                           created_date, modified_date, tax_id, is_tax_inclusive 
                    FROM tbl_product 
                    WHERE product_id = @product_id", connection);

                command.Parameters.AddWithValue("@product_id", productId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new Product
                    {
                        product_id = reader.GetInt32(0),
                        brand_id = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                        category_id = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                        product_name = reader.GetString(3),
                        product_sku = reader.GetString(4),
                        model_number = reader.IsDBNull(5) ? null : reader.GetString(5),
                        cost_price = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                        sell_price = reader.GetDecimal(7),
                        quantity = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        status = reader.IsDBNull(9) ? null : reader.GetBoolean(9),
                        created_date = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                        modified_date = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                        tax_id = reader.IsDBNull(12) ? 1 : reader.GetInt32(12),
                        is_tax_inclusive = reader.IsDBNull(13) ? true : reader.GetBoolean(13)
                    };
                }

                return new Product(); // Return empty product instead of null
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading product: {ex.Message}");
            }
        }

        // READ - Get product by SKU
        public async Task<Product> GetProductBySkuAsync(string sku)
        {
            try
            {
                using var connection = db.GetConnection();
                await connection.OpenAsync();

                var command = new SqlCommand(@"
                    SELECT product_id, brand_id, category_id, product_name, product_sku, 
                           model_number, cost_price, sell_price, quantity, status, 
                           created_date, modified_date, tax_id, is_tax_inclusive 
                    FROM tbl_product 
                    WHERE product_sku = @product_sku", connection);

                command.Parameters.AddWithValue("@product_sku", sku);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new Product
                    {
                        product_id = reader.GetInt32(0),
                        brand_id = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                        category_id = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                        product_name = reader.GetString(3),
                        product_sku = reader.GetString(4),
                        model_number = reader.IsDBNull(5) ? null : reader.GetString(5),
                        cost_price = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                        sell_price = reader.GetDecimal(7),
                        quantity = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        status = reader.IsDBNull(9) ? null : reader.GetBoolean(9),
                        created_date = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                        modified_date = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                        tax_id = reader.IsDBNull(12) ? 1 : reader.GetInt32(12),
                        is_tax_inclusive = reader.IsDBNull(13) ? true : reader.GetBoolean(13)
                    };
                }

                return new Product(); // Return empty product instead of null
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading product: {ex.Message}");
            }
        }
    }
}