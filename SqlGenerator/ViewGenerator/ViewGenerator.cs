using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Text;

public class ViewGenerator
{
    private readonly string _connectionString;
    private readonly HashSet<string> _generatedViewPaths = new HashSet<string>(); // Track unique paths to prevent duplicate views

    public ViewGenerator(string connectionString)
    {
        _connectionString = connectionString;
    }

    public string GenerateViewsForForeignKeys()
    {
        using var connection = new SqlConnection(_connectionString);
        connection.Open();

        var foreignKeys = GetForeignKeys(connection);
        var sqlScripts = new StringBuilder();

        foreach (var foreignKey in foreignKeys)
        {
            // Start recursive generation of views for all levels
            GenerateMultiLevelViews(connection, foreignKey.PrimaryTable, foreignKey.ForeignTable, foreignKey.PrimaryColumn, foreignKey.ForeignColumn, sqlScripts);
        }

        return sqlScripts.ToString();
    }

    private List<ForeignKeyInfo> GetForeignKeys(SqlConnection connection)
    {
        var foreignKeys = new List<ForeignKeyInfo>();

        string query = @"
            SELECT
                fk.name AS ForeignKeyName,
                pk_table.name AS PrimaryTable,
                fk_table.name AS ForeignTable,
                pk_col.name AS PrimaryColumn,
                fk_col.name AS ForeignColumn
            FROM 
                sys.foreign_keys AS fk
            INNER JOIN 
                sys.foreign_key_columns AS fk_cols ON fk.object_id = fk_cols.constraint_object_id
            INNER JOIN 
                sys.tables AS pk_table ON fk_cols.referenced_object_id = pk_table.object_id
            INNER JOIN 
                sys.columns AS pk_col ON fk_cols.referenced_object_id = pk_col.object_id AND fk_cols.referenced_column_id = pk_col.column_id
            INNER JOIN 
                sys.tables AS fk_table ON fk_cols.parent_object_id = fk_table.object_id
            INNER JOIN 
                sys.columns AS fk_col ON fk_cols.parent_object_id = fk_col.object_id AND fk_cols.parent_column_id = fk_col.column_id";

        using var command = new SqlCommand(query, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            foreignKeys.Add(new ForeignKeyInfo
            {
                ForeignKeyName = reader.GetString(0),
                PrimaryTable = reader.GetString(1),
                ForeignTable = reader.GetString(2),
                PrimaryColumn = reader.GetString(3),
                ForeignColumn = reader.GetString(4)
            });
        }

        return foreignKeys;
    }

    private void GenerateMultiLevelViews(SqlConnection connection, string primaryTable, string foreignTable, string primaryColumn, string foreignColumn, StringBuilder sqlScripts, StringBuilder joinClauses = null, StringBuilder selectedColumns = null, HashSet<string> visitedTables = null, StringBuilder path = null, int level = 1)
    {
        visitedTables ??= new HashSet<string>();
        path ??= new StringBuilder(primaryTable);
        joinClauses ??= new StringBuilder();
        selectedColumns ??= new StringBuilder();

        // Avoid cycles and limit depth if needed
        if (visitedTables.Contains(foreignTable) || level > 5)
            return;

        visitedTables.Add(foreignTable);
        path.Append($"_{foreignTable}");

        // Add columns with alias for the primary table only in the first call
        if (level == 1)
        {
            AppendColumnsForTable(connection, primaryTable, selectedColumns);
        }

        // Add columns with alias for the foreign table
        AppendColumnsForTable(connection, foreignTable, selectedColumns);

        // Add the join clause for the current relationship
        joinClauses.AppendLine($"INNER JOIN [{foreignTable}] ON [{primaryTable}].[{primaryColumn}] = [{foreignTable}].[{foreignColumn}]");

        // Generate a view name based on the path
        string viewName = $"{path}_View";

        // Check if this view has already been generated
        if (_generatedViewPaths.Contains(viewName))
        {
            return; // Skip generation if this view path is already processed
        }

        // Mark this path as processed
        _generatedViewPaths.Add(viewName);

        // Remove trailing comma in selectedColumns
        if (selectedColumns.Length > 0)
        {
            selectedColumns.Length--;  // Remove the last comma
        }

        // Build the SQL for the view
        string viewScript = $@"
--------------------------
GO
        CREATE VIEW [{viewName}] AS
        SELECT {selectedColumns.ToString().TrimEnd(',')}
        FROM [{path.ToString().Split('_')[0]}]
        {joinClauses.ToString()} 
GO
--------------------------
    ";

        sqlScripts.AppendLine(viewScript);

        // Recursively generate views for the next level of foreign key relationships
        var relatedKeys = GetRelatedForeignKeys(connection, foreignTable);
        foreach (var relatedKey in relatedKeys)
        {
            if (!visitedTables.Contains(relatedKey.ForeignTable))
            {
                // Pass the current foreign table as the primary table for the next level
                GenerateMultiLevelViews(connection, foreignTable, relatedKey.ForeignTable, relatedKey.PrimaryColumn, relatedKey.ForeignColumn, sqlScripts, new StringBuilder(joinClauses.ToString()), new StringBuilder(selectedColumns.ToString()), new HashSet<string>(visitedTables), new StringBuilder(path.ToString()), level + 1);
            }
        }
    }

    private List<ForeignKeyInfo> GetRelatedForeignKeys(SqlConnection connection, string tableName)
    {
        var relatedForeignKeys = new List<ForeignKeyInfo>();

        string query = @"
            SELECT
                fk.name AS ForeignKeyName,
                pk_table.name AS PrimaryTable,
                fk_table.name AS ForeignTable,
                pk_col.name AS PrimaryColumn,
                fk_col.name AS ForeignColumn
            FROM 
                sys.foreign_keys AS fk
            INNER JOIN 
                sys.foreign_key_columns AS fk_cols ON fk.object_id = fk_cols.constraint_object_id
            INNER JOIN 
                sys.tables AS pk_table ON fk_cols.referenced_object_id = pk_table.object_id
            INNER JOIN 
                sys.columns AS pk_col ON fk_cols.referenced_object_id = pk_col.object_id AND fk_cols.referenced_column_id = pk_col.column_id
            INNER JOIN 
                sys.tables AS fk_table ON fk_cols.parent_object_id = fk_table.object_id
            INNER JOIN 
                sys.columns AS fk_col ON fk_cols.parent_object_id = fk_col.object_id AND fk_cols.parent_column_id = fk_col.column_id
            WHERE 
                pk_table.name = @TableName";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            relatedForeignKeys.Add(new ForeignKeyInfo
            {
                ForeignKeyName = reader.GetString(0),
                PrimaryTable = reader.GetString(1),
                ForeignTable = reader.GetString(2),
                PrimaryColumn = reader.GetString(3),
                ForeignColumn = reader.GetString(4)
            });
        }

        return relatedForeignKeys;
    }

    private void AppendColumnsForTable(SqlConnection connection, string tableName, StringBuilder selectedColumns)
    {
        // Query to get column names for the specified table
        string query = $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@TableName", tableName);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string columnName = reader.GetString(0);
            // Append the column with an alias in the format: [TableName].[ColumnName] AS [TableNameColumn]
            selectedColumns.Append($"[{tableName}].[{columnName}] AS [{tableName}{columnName}], ");
        }
    }
}

public class ForeignKeyInfo
{
    public string ForeignKeyName { get; set; }
    public string PrimaryTable { get; set; }
    public string ForeignTable { get; set; }
    public string PrimaryColumn { get; set; }
    public string ForeignColumn { get; set; }
}
