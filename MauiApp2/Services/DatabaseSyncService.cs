using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MauiApp2.Services
{
    public interface IDatabaseSyncService
    {
        Task<SyncResult> SyncDatabaseAsync(string localConnectionString, string cloudConnectionString);
        Task<bool> TestConnectionAsync(string connectionString);
    }

    public class DatabaseSyncService : IDatabaseSyncService
    {
        // Tables in order (respecting foreign key dependencies)
        private static readonly List<string> Tables = new List<string>
        {
            "tbl_roles",
            "tbl_users",
            "tbl_category",
            "tbl_brand",
            "tbl_tax",
            "tbl_product",
            "tbl_supplier",
            "tbl_purchase_order",
            "tbl_purchase_order_items",
            "tbl_stock_in",
            "tbl_stock_in_items",
            "tbl_sales_order",
            "tbl_sales_order_items",
            "tbl_stock_out",
            "tbl_stock_out_items"
        };

        public async Task<bool> TestConnectionAsync(string connectionString)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<SyncResult> SyncDatabaseAsync(string localConnectionString, string cloudConnectionString)
        {
            var result = new SyncResult();
            result.StartTime = DateTime.Now;

            try
            {
                // Test connections
                result.Messages.Add("Testing local database connection...");
                if (!await TestConnectionAsync(localConnectionString))
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "Failed to connect to local database";
                    return result;
                }
                result.Messages.Add("✓ Local database connection successful");

                result.Messages.Add("Testing cloud database connection...");
                if (!await TestConnectionAsync(cloudConnectionString))
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "Failed to connect to cloud database";
                    return result;
                }
                result.Messages.Add("✓ Cloud database connection successful");

                result.Messages.Add("");
                result.Messages.Add("Starting data synchronization...");

                // Sync each table
                foreach (var tableName in Tables)
                {
                    var tableResult = await SyncTableAsync(localConnectionString, cloudConnectionString, tableName);
                    result.TotalTablesProcessed++;
                    result.TotalRowsCopied += tableResult.RowsCopied;
                    
                    if (tableResult.IsSuccess)
                    {
                        result.Messages.Add($"✓ {tableName}: {tableResult.RowsCopied} rows copied");
                    }
                    else
                    {
                        result.Messages.Add($"✗ {tableName}: {tableResult.ErrorMessage}");
                        result.HasWarnings = true;
                    }
                }

                result.IsSuccess = true;
                result.Messages.Add("");
                result.Messages.Add($"=== Sync Complete ===");
                result.Messages.Add($"Total tables processed: {result.TotalTablesProcessed}");
                result.Messages.Add($"Total rows copied: {result.TotalRowsCopied}");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
                result.Messages.Add($"ERROR: {ex.Message}");
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        private async Task<TableSyncResult> SyncTableAsync(string localConnectionString, string cloudConnectionString, string tableName)
        {
            var result = new TableSyncResult();

            try
            {
                using var localConn = new SqlConnection(localConnectionString);
                using var cloudConn = new SqlConnection(cloudConnectionString);

                await localConn.OpenAsync();
                await cloudConn.OpenAsync();

                // Check if table exists in local database
                var tableExistsQuery = @"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME = @tableName";

                using var checkCmd = new SqlCommand(tableExistsQuery, localConn);
                checkCmd.Parameters.AddWithValue("@tableName", tableName);
                var tableExists = (int)await checkCmd.ExecuteScalarAsync() > 0;

                if (!tableExists)
                {
                    result.ErrorMessage = "Table doesn't exist in local database";
                    return result;
                }

                // Get all data from local database
                var selectQuery = $"SELECT * FROM [{tableName}]";
                using var localCmd = new SqlCommand(selectQuery, localConn);
                using var reader = await localCmd.ExecuteReaderAsync();

                if (!reader.HasRows)
                {
                    result.RowsCopied = 0;
                    result.IsSuccess = true;
                    return result;
                }

                // Get column names
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }

                // Check if table has IDENTITY column
                var hasIdentity = await HasIdentityColumnAsync(cloudConn, tableName);
                if (hasIdentity)
                {
                    var identityQuery = $"SET IDENTITY_INSERT [{tableName}] ON";
                    using var identityCmd = new SqlCommand(identityQuery, cloudConn);
                    await identityCmd.ExecuteNonQueryAsync();
                }

                int rowCount = 0;
                while (await reader.ReadAsync())
                {
                    // Build INSERT statement
                    var columnList = string.Join(", ", columns.Select(c => $"[{c}]"));
                    var valueList = string.Join(", ", columns.Select(c => $"@{c}"));

                    var insertQuery = $"INSERT INTO [{tableName}] ({columnList}) VALUES ({valueList})";

                    // Check if row already exists (by primary key)
                    var pkColumn = await GetPrimaryKeyColumnAsync(cloudConn, tableName);
                    if (!string.IsNullOrEmpty(pkColumn))
                    {
                        var pkValue = reader[pkColumn];
                        var rowExistsQuery = $"SELECT COUNT(*) FROM [{tableName}] WHERE [{pkColumn}] = @pkValue";
                        using var rowExistsCmd = new SqlCommand(rowExistsQuery, cloudConn);
                        rowExistsCmd.Parameters.AddWithValue("@pkValue", pkValue);
                        var rowExists = (int)await rowExistsCmd.ExecuteScalarAsync() > 0;

                        if (rowExists)
                        {
                            continue; // Skip if already exists
                        }
                    }

                    using var insertCmd = new SqlCommand(insertQuery, cloudConn);

                    // Add parameters
                    foreach (var column in columns)
                    {
                        var value = reader[column];
                        if (value == DBNull.Value)
                        {
                            insertCmd.Parameters.AddWithValue($"@{column}", DBNull.Value);
                        }
                        else
                        {
                            insertCmd.Parameters.AddWithValue($"@{column}", value);
                        }
                    }

                    await insertCmd.ExecuteNonQueryAsync();
                    rowCount++;
                }

                if (hasIdentity)
                {
                    var identityQuery = $"SET IDENTITY_INSERT [{tableName}] OFF";
                    using var identityCmd = new SqlCommand(identityQuery, cloudConn);
                    await identityCmd.ExecuteNonQueryAsync();
                }

                result.RowsCopied = rowCount;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<bool> HasIdentityColumnAsync(SqlConnection connection, string tableName)
        {
            try
            {
                var query = @"
                    SELECT COUNT(*) 
                    FROM sys.columns c
                    INNER JOIN sys.tables t ON c.object_id = t.object_id
                    WHERE t.name = @tableName 
                    AND c.is_identity = 1";

                using var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@tableName", tableName);
                var count = (int)await cmd.ExecuteScalarAsync();
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> GetPrimaryKeyColumnAsync(SqlConnection connection, string tableName)
        {
            try
            {
                var query = @"
                    SELECT c.name
                    FROM sys.key_constraints kc
                    INNER JOIN sys.index_columns ic ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
                    INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                    INNER JOIN sys.tables t ON kc.parent_object_id = t.object_id
                    WHERE t.name = @tableName
                    AND kc.type = 'PK'";

                using var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@tableName", tableName);
                var result = await cmd.ExecuteScalarAsync();
                return result?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public class SyncResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<string> Messages { get; set; } = new List<string>();
        public int TotalTablesProcessed { get; set; }
        public int TotalRowsCopied { get; set; }
        public bool HasWarnings { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class TableSyncResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public int RowsCopied { get; set; }
    }
}
