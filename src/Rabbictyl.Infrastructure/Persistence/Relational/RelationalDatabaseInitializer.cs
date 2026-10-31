using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Rabbictyl.Core.Abstractions;

namespace Rabbictyl.Infrastructure.Persistence.Relational;

/// <summary>
/// Creates the schema on a fresh database and reconciles it on existing ones.
/// The panel ships without generated migrations, so a database created by an
/// older build would otherwise miss columns and tables added since; every
/// upgrade would fail with "no such column".
/// </summary>
public sealed partial class RelationalDatabaseInitializer : IDatabaseInitializer
{
    private readonly PanelDbContext _db;

    public RelationalDatabaseInitializer(PanelDbContext db)
    {
        _db = db;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var migrations = _db.Database.GetMigrations();

        if (migrations.Any())
        {
            await _db.Database.MigrateAsync(cancellationToken);
            return;
        }

        // Until the first migration is generated we create the schema directly.
        // Once migrations exist the branch above takes over automatically.
        await _db.Database.EnsureCreatedAsync(cancellationToken);

        await ReconcileAsync(cancellationToken);
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var creator = _db.Database.GetService<IRelationalDatabaseCreator>();
        var helper = _db.Database.GetService<ISqlGenerationHelper>();
        var tables = await ReadTablesAsync(cancellationToken);

        foreach (var statement in SplitStatements(creator.GenerateCreateScript()))
        {
            var match = CreateTablePattern().Match(statement);

            if (!match.Success)
            {
                continue;
            }

            var table = match.Groups["name"].Value;

            if (tables.Contains(table))
            {
                continue;
            }

            await _db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
            tables.Add(table);
        }

        foreach (var entity in _db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();

            if (table is null)
            {
                continue;
            }

            var storeObject = StoreObjectIdentifier.Table(table, entity.GetSchema());
            var columns = await ReadColumnsAsync(table, cancellationToken);

            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(storeObject);

                if (column is null || columns.Contains(column))
                {
                    continue;
                }

                var defaultSql = DefaultLiteral(property.ClrType);
                var addColumn = new System.Text.StringBuilder()
                    .Append("ALTER TABLE ")
                    .Append(helper.DelimitIdentifier(table))
                    .Append(" ADD COLUMN ")
                    .Append(helper.DelimitIdentifier(column))
                    .Append(' ')
                    .Append(property.GetColumnType())
                    .Append(" NULL");

                if (defaultSql is not null)
                {
                    addColumn.Append(" DEFAULT ").Append(defaultSql);
                }

                await _db.Database.ExecuteSqlRawAsync(addColumn.ToString(), cancellationToken);
                columns.Add(column);
            }
        }
    }

    private async Task<HashSet<string>> ReadTablesAsync(CancellationToken cancellationToken)
    {
        var sql = ProviderName switch
        {
            var name when name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) =>
                "SELECT name AS \"Value\" FROM sqlite_master WHERE type = 'table'",
            var name when name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) =>
                "SELECT tablename AS \"Value\" FROM pg_tables WHERE schemaname = 'public'",
            _ => "SELECT TABLE_NAME AS Value FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()",
        };

        var names = await _db.Database.SqlQueryRaw<string>(sql).ToListAsync(cancellationToken);

        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<HashSet<string>> ReadColumnsAsync(string table, CancellationToken cancellationToken)
    {
        var sql = ProviderName switch
        {
            var name when name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) =>
                $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\")",
            var name when name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) =>
                $"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table.Replace("'", "''")}'",
            _ =>
                $"SELECT COLUMN_NAME AS Value FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table.Replace("'", "''")}'",
        };

        if (ProviderName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            // PRAGMA returns several columns, so read the name column by position.
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            if (command.Connection?.State != System.Data.ConnectionState.Open)
            {
                await command.Connection!.OpenAsync(cancellationToken);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }

            return columns;
        }

        var names = await _db.Database.SqlQueryRaw<string>(sql).ToListAsync(cancellationToken);

        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    private string ProviderName => _db.Database.ProviderName ?? string.Empty;

    private static string? DefaultLiteral(Type type)
    {
        if (type == typeof(bool))
        {
            return "0";
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(decimal) ||
            type == typeof(double) || type == typeof(float))
        {
            return "0";
        }

        if (type == typeof(Guid))
        {
            return "''";
        }

        if (type == typeof(DateTimeOffset) || type == typeof(DateTime))
        {
            return null;
        }

        return null;
    }

    private static IEnumerable<string> SplitStatements(string script) =>
        script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(statement => statement.Length > 0);

    [GeneratedRegex(
        "^CREATE TABLE (?:IF NOT EXISTS )?[\"`\\[]?(?<name>[A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CreateTablePattern();
}
