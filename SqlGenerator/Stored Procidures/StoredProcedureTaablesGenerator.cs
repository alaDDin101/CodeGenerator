using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Text;

namespace SqlGenerator.Stored_Procedures
{
    public class StoredProcedureGenerator
    {
        private readonly string _connectionString;

        public StoredProcedureGenerator(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string GenerateStoredProceduresForAllTables()
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            var tableNames = GetAllTableNames(connection);
            var sqlScripts = new StringBuilder();
            var generatedProcedureNames = new HashSet<string>(); // Track unique procedure names

            foreach (var tableName in tableNames)
            {
                var procedures = GenerateStoredProceduresForTable(connection, tableName, generatedProcedureNames);
                if (!string.IsNullOrEmpty(procedures))
                {
                    sqlScripts.AppendLine($"-- Stored Procedures for Table: [{tableName}]");
                    sqlScripts.AppendLine(procedures);
                    sqlScripts.AppendLine();
                }
            }

            return sqlScripts.ToString();
        }

        private string GenerateStoredProceduresForTable(SqlConnection connection, string tableName, HashSet<string> generatedProcedureNames)
        {
            var primaryKeyColumn = GetPrimaryKeyColumn(connection, tableName);

            if (string.IsNullOrEmpty(primaryKeyColumn))
            {
                return null; // Skip tables without a primary key
            }

            var isPrimaryKeyAutoIncrement = IsAutoIncrementColumn(connection, tableName, primaryKeyColumn);
            var foreignKeys = GetForeignKeys(connection, tableName);
            var columns = GetTableColumns(connection, tableName);

            var sqlScripts = new StringBuilder();

            // Generate CRUD procedures, ensuring uniqueness by name
            GenerateProcedureIfUnique(sqlScripts, GenerateGetAllProcedure(tableName, columns), $"GetAll{tableName}", generatedProcedureNames);
            GenerateProcedureIfUnique(sqlScripts, GenerateGetByPrimaryKeyProcedure(tableName, primaryKeyColumn, columns), $"Get{tableName}By{primaryKeyColumn}", generatedProcedureNames);
            GenerateProcedureIfUnique(sqlScripts, GenerateInsertProcedure(tableName, columns, primaryKeyColumn, isPrimaryKeyAutoIncrement), $"Insert{tableName}", generatedProcedureNames);
            GenerateProcedureIfUnique(sqlScripts, GenerateUpdateProcedure(tableName, primaryKeyColumn, columns), $"Update{tableName}By{primaryKeyColumn}", generatedProcedureNames);
            GenerateProcedureIfUnique(sqlScripts, GenerateDeleteByPrimaryKeyProcedure(tableName, primaryKeyColumn), $"Delete{tableName}By{primaryKeyColumn}", generatedProcedureNames);

            // Generate procedures for foreign key operations
            foreach (var foreignKey in foreignKeys)
            {
                GenerateProcedureIfUnique(sqlScripts, GenerateGetByForeignKeyProcedure(tableName, foreignKey), $"Get{tableName}By{foreignKey.ForeignKeyColumn}", generatedProcedureNames);
                GenerateProcedureIfUnique(sqlScripts, GenerateDeleteByForeignKeyProcedure(tableName, foreignKey), $"Delete{tableName}By{foreignKey.ForeignKeyColumn}", generatedProcedureNames);
            }

            return sqlScripts.ToString();
        }

        private void GenerateProcedureIfUnique(StringBuilder sqlScripts, string procedure, string procedureName, HashSet<string> generatedProcedureNames)
        {
            if (!generatedProcedureNames.Contains(procedureName))
            {
                generatedProcedureNames.Add(procedureName);
                sqlScripts.AppendLine(procedure);
            }
        }

        private string GenerateGetAllProcedure(string tableName, List<ColumnInfo> columns)
        {
            var columnNames = string.Join(", ", columns.ConvertAll(col => $"[{col.ColumnName}]"));
            return $@"
GO
            CREATE PROCEDURE GetAll{tableName}
            AS
            BEGIN
                SELECT {columnNames} FROM [{tableName}];
            END
GO
        ";
        }

        private string GenerateGetByPrimaryKeyProcedure(string tableName, string primaryKeyColumn, List<ColumnInfo> columns)
        {
            var columnNames = string.Join(", ", columns.ConvertAll(col => $"[{col.ColumnName}]"));
            return $@"
GO
            CREATE PROCEDURE Get{tableName}By{primaryKeyColumn}
                @{primaryKeyColumn} INT
            AS
            BEGIN
                SELECT {columnNames} FROM [{tableName}]
                WHERE [{primaryKeyColumn}] = @{primaryKeyColumn};
            END
GO
        ";
        }

        private string GenerateInsertProcedure(string tableName, List<ColumnInfo> columns, string primaryKeyColumn, bool isPrimaryKeyAutoIncrement)
        {
            var columnsToInsert = isPrimaryKeyAutoIncrement ? columns.FindAll(col => col.ColumnName != primaryKeyColumn) : columns;
            var columnNames = string.Join(", ", columnsToInsert.ConvertAll(col => $"[{col.ColumnName}]"));
            var paramNames = string.Join(", ", columnsToInsert.ConvertAll(col => "@" + col.ColumnName));
            var parameterDefinitions = string.Join(", ", columnsToInsert.ConvertAll(col => $"@{col.ColumnName} {col.DataType}"));

            return $@"
GO
            CREATE PROCEDURE Insert{tableName}
                {parameterDefinitions}
            AS
            BEGIN
                INSERT INTO [{tableName}] ({columnNames})
                VALUES ({paramNames});
                SELECT SCOPE_IDENTITY() AS NewID;
            END
GO
        ";
        }

        private string GenerateUpdateProcedure(string tableName, string primaryKeyColumn, List<ColumnInfo> columns)
        {
            var setClause = string.Join(", ", columns.FindAll(col => col.ColumnName != primaryKeyColumn)
                                                     .ConvertAll(col => $"[{col.ColumnName}] = @{col.ColumnName}"));
            var parameterDefinitions = $"@{primaryKeyColumn} INT, " +
                                       string.Join(", ", columns.FindAll(col => col.ColumnName != primaryKeyColumn)
                                                                 .ConvertAll(col => $"@{col.ColumnName} {col.DataType}"));

            return $@"
GO
            CREATE PROCEDURE Update{tableName}By{primaryKeyColumn}
                {parameterDefinitions}
            AS
            BEGIN
                UPDATE [{tableName}]
                SET {setClause}
                WHERE [{primaryKeyColumn}] = @{primaryKeyColumn};
            END
GO
        ";
        }

        private string GenerateDeleteByPrimaryKeyProcedure(string tableName, string primaryKeyColumn)
        {
            return $@"
GO
            CREATE PROCEDURE Delete{tableName}By{primaryKeyColumn}
                @{primaryKeyColumn} INT
            AS
            BEGIN
                DELETE FROM [{tableName}] WHERE [{primaryKeyColumn}] = @{primaryKeyColumn};
            END
GO
        ";
        }

        private string GenerateGetByForeignKeyProcedure(string tableName, ForeignKeyInfo foreignKey)
        {
            return $@"

GO
            CREATE PROCEDURE Get{tableName}By{foreignKey.ForeignKeyColumn}
                @{foreignKey.ForeignKeyColumn} INT
            AS
            BEGIN
                SELECT * FROM [{tableName}] WHERE [{foreignKey.ForeignKeyColumn}] = @{foreignKey.ForeignKeyColumn};
            END
GO
        ";
        }

        private string GenerateDeleteByForeignKeyProcedure(string tableName, ForeignKeyInfo foreignKey)
        {
            return $@"
GO
            CREATE PROCEDURE Delete{tableName}By{foreignKey.ForeignKeyColumn}
                @{foreignKey.ForeignKeyColumn} INT
            AS
            BEGIN
                DELETE FROM [{tableName}] WHERE [{foreignKey.ForeignKeyColumn}] = @{foreignKey.ForeignKeyColumn};
            END
GO
        ";
        }

        private List<string> GetAllTableNames(SqlConnection connection)
        {
            var tableNames = new List<string>();
            var query = @"
            SELECT TABLE_NAME 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_TYPE = 'BASE TABLE' 
            AND TABLE_NAME != 'sysdiagrams'";

            using var command = new SqlCommand(query, connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tableNames.Add(reader.GetString(0));
            }

            return tableNames;
        }

        private string GetPrimaryKeyColumn(SqlConnection connection, string tableName)
        {
            var query = @"
            SELECT COLUMN_NAME 
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
            WHERE TABLE_NAME = @TableName AND OBJECTPROPERTY(OBJECT_ID(CONSTRAINT_SCHEMA + '.' + CONSTRAINT_NAME), 'IsPrimaryKey') = 1";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@TableName", tableName);

            var result = command.ExecuteScalar();
            return result?.ToString();
        }

        private bool IsAutoIncrementColumn(SqlConnection connection, string tableName, string columnName)
        {
            var query = @"
            SELECT COLUMNPROPERTY(OBJECT_ID(@TableName), @ColumnName, 'IsIdentity') AS IsIdentity";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@TableName", tableName);
            command.Parameters.AddWithValue("@ColumnName", columnName);

            var result = command.ExecuteScalar();
            return result != null && (int)result == 1;
        }

        private List<ColumnInfo> GetTableColumns(SqlConnection connection, string tableName)
        {
            var columns = new List<ColumnInfo>();
            var query = "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@TableName", tableName);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(new ColumnInfo
                {
                    ColumnName = reader["COLUMN_NAME"].ToString(),
                    DataType = reader["DATA_TYPE"].ToString()
                });
            }

            return columns;
        }

        private List<ForeignKeyInfo> GetForeignKeys(SqlConnection connection, string tableName)
        {
            var foreignKeys = new List<ForeignKeyInfo>();

            string query = @"
            SELECT 
                fk.name AS ForeignKeyName,
                c.name AS ForeignKeyColumn,
                rt.name AS PrimaryKeyTable
            FROM sys.foreign_keys AS fk
            INNER JOIN sys.foreign_key_columns AS fkc 
                ON fk.object_id = fkc.constraint_object_id
            INNER JOIN sys.columns AS c
                ON fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
            INNER JOIN sys.tables AS rt
                ON fkc.referenced_object_id = rt.object_id
            WHERE OBJECT_NAME(fk.parent_object_id) = @TableName";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@TableName", tableName);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                foreignKeys.Add(new ForeignKeyInfo
                {
                    ForeignKeyName = reader["ForeignKeyName"].ToString(),
                    ForeignKeyColumn = reader["ForeignKeyColumn"].ToString(),
                    PrimaryKeyTable = reader["PrimaryKeyTable"].ToString()
                });
            }

            return foreignKeys;
        }
    }

    public class ColumnInfo
    {
        public string ColumnName { get; set; }
        public string DataType { get; set; }
    }

    public class ForeignKeyInfo
    {
        public string ForeignKeyName { get; set; }
        public string ForeignKeyColumn { get; set; }
        public string PrimaryKeyTable { get; set; }
    }
}
