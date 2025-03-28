using Dapper;
using ExcelToDB_Backend.Models;
using Microsoft.Data.SqlClient;


namespace ExcelToDB_Backend.Services
{
    public interface IDatabaseService
    {
        Task<dynamic> GetAllDatabases(string connectionString);
        Task<dynamic> GetAllTables(string connectionString, string databaseName);
        Task<dynamic> InsertValues(string connectionString, string databaseName, string tableName, List<List<string>> Data);
    }
    public class DatabaseService : IDatabaseService
    {
        public async Task<dynamic> GetAllDatabases(string connectionString)
        {
            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    var query = "SELECT name FROM sys.databases WHERE database_id > 4";
                    var result = await connection.QueryAsync(query);
                    return new
                    {
                        statusCode = 200,
                        result = result
                    };
                }
            }
            catch (System.Exception ex)
            {
                return new
                {
                    statusCode = 500,
                    message = "Không kết nối được database"
                };
            }

        }

        public async Task<dynamic> GetAllTables(string connectionString, string databaseName)
        {
            Console.WriteLine($"{connectionString};Initial Catalog={databaseName}");
            using var connection = new SqlConnection($"{connectionString};Initial Catalog={databaseName}");
            var query = "SELECT TABLE_NAME as name FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
            return await connection.QueryAsync(query);
        }

        private async Task<dynamic> GetColumnNameInTable(string connectionString, string databaseName, string tableName)
        {
            using var connection = new SqlConnection($"{connectionString};Initial Catalog={databaseName}");
            {
                var query = @"SELECT COLUMN_NAME as name
                                FROM INFORMATION_SCHEMA.COLUMNS 
                                WHERE TABLE_NAME = @TableName 
                                AND COLUMN_NAME LIKE 'Ten%';
                                ";
                var parameters = new { TableName = tableName };
                return await connection.QueryFirstOrDefaultAsync(query, parameters);
            }
        }

        private async Task<List<ForeignKey>> GetForeignKey(string connectionString, string databaseName, string tableName)
        {
            using (var connection = new SqlConnection($"{connectionString};Initial Catalog={databaseName}"))
            {
                var query = @"
                    SELECT 
                        kcu.COLUMN_NAME AS ForeignKeyColumn,  -- Cột khóa ngoại trong bảng chính
                        kcu.REFERENCED_TABLE_NAME AS ReferencedTable,  -- Bảng được tham chiếu
                        kcu.REFERENCED_COLUMN_NAME AS ReferencedColumn -- Cột được tham chiếu trong bảng kia
                    FROM (
                        SELECT 
                            fk.name AS CONSTRAINT_NAME,
                            tp.name AS TABLE_NAME,
                            cp.name AS COLUMN_NAME,
                            tr.name AS REFERENCED_TABLE_NAME,
                            cr.name AS REFERENCED_COLUMN_NAME
                        FROM sys.foreign_keys fk
                        INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                        INNER JOIN sys.tables tp ON tp.object_id = fkc.parent_object_id
                        INNER JOIN sys.columns cp ON cp.column_id = fkc.parent_column_id AND cp.object_id = tp.object_id
                        INNER JOIN sys.tables tr ON tr.object_id = fkc.referenced_object_id
                        INNER JOIN sys.columns cr ON cr.column_id = fkc.referenced_column_id AND cr.object_id = tr.object_id
                    ) kcu
                    WHERE kcu.TABLE_NAME =  @tableName
                    ";
                var parameters = new { tableName = tableName };
                return (await connection.QueryAsync<ForeignKey>(query, parameters)).ToList();
            }
        }
        private async Task<int?> GetItemOrCreateNew(string connectionString, string? value, string tableName, string databaseName, string columnName, string referencedColumn)
        {
            if (value == null) return null;
            using (var connection = new SqlConnection($"{connectionString};Initial Catalog={databaseName}"))
            {
                var query = $"select {referencedColumn} from {tableName} where {columnName} = N'{value}'";
                int? itemId = await connection.QueryFirstOrDefaultAsync<int?>(query);

                if (itemId.HasValue)
                {
                    return itemId;
                }

                var insertQuery = $"INSERT INTO {tableName} ({columnName}) OUTPUT INSERTED.Id{tableName} VALUES (@value)";
                int newId = await connection.QuerySingleAsync<int>(insertQuery, new { value });

                return newId;
            }
        }

        public async Task<dynamic> InsertValues(string connectionString, string databaseName, string tableName, List<List<string>> Data)
        {
            var foreignKeys = await GetForeignKey(connectionString, databaseName, tableName);

            var columnTables = Data.First();

            var matchedColumns = columnTables
                .Select((col, index) => new { Column = col, Index = index })
                .Where(item => foreignKeys.Any(fk => item.Column.Contains(fk.ForeignKeyColumn)))
                .Select(item => new { Value = item.Column, index = item.Index, foreignKey = foreignKeys.Where(fk => fk.ForeignKeyColumn == item.Column).FirstOrDefault() })
                .ToList();

            var listNotForeignKey = columnTables.Where(col => matchedColumns.All(fk => !string.Equals(fk.Value, col, StringComparison.Ordinal))).ToList();

            Dictionary<string, List<int?>> idOfForeignKeys = [];

            foreach (var items in Data.Skip(1))
            {
                foreach (var value in matchedColumns)
                {
                    var referencedTable = value.foreignKey.ReferencedTable;
                    var referencedColumn = value.foreignKey.ReferencedColumn;
                    var columnName = await GetColumnNameInTable(connectionString, databaseName, referencedTable);

                    if (!idOfForeignKeys.ContainsKey(value.Value))
                    {
                        idOfForeignKeys[value.Value] = new List<int?>();
                    }
                    var id = await GetItemOrCreateNew(connectionString, value.index < items.Count ? items[value.index] : null, referencedTable, databaseName, columnName.name, referencedColumn);
                    idOfForeignKeys[value.Value].Add(id);
                }
            }

            string columnToInsert = string.Join(",", listNotForeignKey);
            columnToInsert = columnToInsert + "," + string.Join(",", idOfForeignKeys.Keys);

            List<string> values = [];
            using (var connection = new SqlConnection($"{connectionString};Initial Catalog={databaseName}"))
            {
                for (int i = 1; i < Data.Count; i++)
                {
                    var valuesList = Data[i]
                        .Where((col, index) => !matchedColumns.Any(u => u.index == index))
                        .ToList();

                    var idValues = idOfForeignKeys.Count != 0
                        ? "," + string.Join(",", idOfForeignKeys.Select(kv => i <= kv.Value.Count ? kv.Value[i - 1] == null ? "null" : kv.Value[i - 1].ToString() : ""))
                        : "";

                    var value = string.Join(",", valuesList.Select(u => "N'" + u + "'")) + idValues;
                    values.Add(value);
                }
            }

            var result = InsertToDatabase(connectionString, databaseName, tableName, columnToInsert, values, Data);

            return result;
        }

        private dynamic InsertToDatabase(string connectionString, string databaseName, string tableName, string columnToInsert, List<string> values, List<List<string>> Data)
        {
            try
            {
                using (var connection = new SqlConnection($"{connectionString};Initial Catalog={databaseName}"))
                {
                    var query = $"INSERT INTO {tableName}({columnToInsert}) VALUES ";
                    for (int i = 1; i < Data.Count; i++)
                    {
                        if (i != 1) query += ",";
                        query += $"({values[i - 1]})";
                        var parameters = values.Select(value => new { Value = value }).ToList();
                    }
                    var rowEffect = connection.Execute(query);
                    if (rowEffect > 0) return new { statusCode = 200, message = "Thêm danh sách thành công" };
                    return new { statusCode = 400, message = "Thêm danh sách thất bại" };
                }
            }
            catch (SqlException ex)
            {
                return new { statusCode = 500, message = ex.Message };
            }

        }
    }
}