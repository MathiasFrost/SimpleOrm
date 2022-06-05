using System.Data;
using System.Data.Common;
using JetBrains.Annotations;

namespace SimpleOrm;

[PublicAPI]
public class SimpleOrmClient<TDbConnection> where TDbConnection : DbConnection, new()
{
	private readonly string _connectionString;

	public SimpleOrmClient(string connectionString) => _connectionString = connectionString;

	public List<T> Query<T>(string sql) where T : new() =>
			QueryAsync<T>(sql).ConfigureAwait(false).GetAwaiter().GetResult();

	public async Task<List<T>> QueryAsync<T>(string sql, CancellationToken token = default) where T : new()
	{
		await using var connection = new TDbConnection();
		connection.ConnectionString = _connectionString;
		DbCommand command = connection.CreateCommand();
		command.CommandText = sql;
		if (command.Connection == null)
		{
			return new List<T>();
		}
		await command.Connection.OpenAsync(token).ConfigureAwait(false);

		var rows = new List<object[]>();
		IList<PropertyHierarchy> properties = Array.Empty<PropertyHierarchy>();
		var first = true;
		await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
		while (await reader.ReadAsync(token).ConfigureAwait(false))
		{
			var row = new object[reader.FieldCount];
			reader.GetValues(row);
			if (first)
			{
				properties = reader.BuildHierarchy<T>(row);
			}
			rows.Add(row);
			first = false;
		}

		await reader.CloseAsync().ConfigureAwait(false);
		return new List<T>();
	}

	public async Task<T?> FirstOrDefault<T>(string sql, CancellationToken token = default) where T : new()
	{
		await using var connection = new TDbConnection();
		connection.ConnectionString = _connectionString;
		DbCommand command = connection.CreateCommand();
		command.CommandText = sql;
		if (command.Connection == null)
		{
			return default(T?);
		}
		await command.Connection.OpenAsync(token).ConfigureAwait(false);

		var res = new T();
		var first = true;
		IList<PropertyHierarchy> properties = Array.Empty<PropertyHierarchy>();
		await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
		while (await reader.ReadAsync(token).ConfigureAwait(false))
		{
			var row = new object[reader.FieldCount];
			reader.GetValues(row);
			if (first)
			{
				properties = reader.BuildHierarchy<T>(row);
			}
			res.Parse(row, properties);
			first = false;
		}

		await reader.CloseAsync().ConfigureAwait(false);
		return res;
	}
}