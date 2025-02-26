using Dapper;
using ExcelToDB_Backend.Models;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;


namespace ExcelToDB_Backend.Services
{
    public interface IDatabaseService
    {
        Task<dynamic> GetAllDatabases();
        Task<dynamic> GetAllTables(string databaseName);
        Task<dynamic> InsertValues(string databaseName, string tableName, List<List<string>> Data);
    }
    public class DatabaseService : IDatabaseService
    {
        private readonly string _connectionString = "Server=localhost;User Id=sa;Password=123456aA@$;TrustServerCertificate=True";
        public async Task<dynamic> GetAllDatabases()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                var query = "SELECT name FROM sys.databases WHERE database_id > 4";
                return await connection.QueryAsync(query);
            }
        }

        public async Task<dynamic> GetAllTables(string databaseName)
        {
            using var connection = new SqlConnection($"{_connectionString};Initial Catalog={databaseName}");
            var query = "SELECT TABLE_NAME as name FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
            return await connection.QueryAsync(query);
        }

        private async Task<dynamic> GetColumnNameInTable(string databaseName, string tableName)
        {
            using var connection = new SqlConnection($"{_connectionString};Initial Catalog={databaseName}");
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

        private async Task<List<ForeignKey>> GetForeignKey(string databaseName, string tableName)
        {
            using (var connection = new SqlConnection($"{_connectionString};Initial Catalog={databaseName}"))
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
        private async Task<int?> GetItemOrCreateNew(string? value, string tableName, string databaseName, string columnName)
        {
            // Console.WriteLine(tableName);
            if (value == null) return null;
            using (var connection = new SqlConnection($"{_connectionString};Initial Catalog={databaseName}"))
            {
                var query = $"select Id{tableName} from {tableName} where {columnName} = N'{value}'";
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

        public async Task<dynamic> InsertValues(string databaseName, string tableName, List<List<string>> Data)
        {
            var foreignKeys = await GetForeignKey(databaseName, tableName);

            var columnTables = Data.First();

            var matchedColumns = columnTables
                .Select((col, index) => new { Column = col, Index = index })
                .Where(item => foreignKeys.Any(fk => item.Column.Contains(fk.ReferencedTable)))
                .Select(item => new { Value = item.Column, index = item.Index })
                .ToList();

            // return matchedColumns;

            var listNotForeignKey = columnTables.Where(col => matchedColumns.All(fk => !string.Equals(fk.Value, col, StringComparison.Ordinal))).ToList();
            // return listNotForeignKey;

            Dictionary<string, List<int?>> idOfForeignKeys = [];

            foreach (var items in Data.Skip(1))
            {
                foreach (var value in matchedColumns)
                {
                    var tableNameOfForeignKey = value.Value.Replace("Id", "");
                    var columnName = await GetColumnNameInTable(databaseName, tableNameOfForeignKey);

                    if (!idOfForeignKeys.ContainsKey(value.Value))
                    {
                        idOfForeignKeys[value.Value] = new List<int?>();
                    }
                    var id = await GetItemOrCreateNew(value.index < items.Count ? items[value.index] : null, tableNameOfForeignKey, databaseName, columnName.name);
                    idOfForeignKeys[value.Value].Add(id);
                }
            }

            string columnToInsert = string.Join(",", listNotForeignKey);
            columnToInsert = columnToInsert + "," + string.Join(",", idOfForeignKeys.Keys);

            List<string> values = [];
            using (var connection = new SqlConnection($"{_connectionString};Initial Catalog={databaseName}"))
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
            // return new { values = values, column = columnToInsert };

            var result = InsertToDatabase(databaseName, tableName, columnToInsert, values, Data);

            return new
            {
                statusCode = result ? 200 : 400,
                message = result ? "Thêm danh sách thành công !" : "Thêm danh sách thất bại !"
            };
        }

        private bool InsertToDatabase(string databaseName, string tableName, string columnToInsert, List<string> values, List<List<string>> Data)
        {
            using (var connection = new SqlConnection($"{_connectionString};Initial Catalog={databaseName}"))
            {
                var query = $"INSERT INTO {tableName}({columnToInsert}) VALUES ";
                for (int i = 1; i < Data.Count; i++)
                {
                    if (i != 1) query += ",";
                    query += $"({values[i - 1]})";
                    var parameters = values.Select(value => new { Value = value }).ToList();
                }
                var rowEffect = connection.Execute(query);
                if (rowEffect > 0) return true;
                return false;
            }
        }

        // private static string FormatColumnName(string data)
        // {
        //     string normalized = data.Normalize(NormalizationForm.FormD);

        //     string withoutDiacritics = new string(normalized
        //         .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
        //         .ToArray());

        //     string cleaned = Regex.Replace(withoutDiacritics, @"[^a-zA-Z0-9\s]", "");

        //     cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        //     TextInfo textInfo = CultureInfo.CurrentCulture.TextInfo;
        //     return textInfo.ToTitleCase(cleaned.ToLower()).Replace(" ", "");
        // }

    }
}