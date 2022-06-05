using System.Data;
using System.Data.Common;
using JetBrains.Annotations;
using SimpleOrm.Helpers;
using SimpleOrm.Models;

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

		var res = new List<T>();
		var first = true;
		object[] prev = Array.Empty<object>();
		var properties = new List<PropertyHierarchy>();
		await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
		while (await reader.ReadAsync(token).ConfigureAwait(false))
		{
			var row = new object[reader.FieldCount];
			reader.GetValues(row);
			properties.ForEach(p => p.Reset(prev, row));
			if (first)
			{
				properties = reader.BuildHierarchy<T>(row);
			}

			T? item = res.GetByKeys(properties, row);
			if (item == null)
			{
				item = new T();
				res.Add(item);
				properties.ForEach(p => p.Reset());
			}
			item.Parse(row, properties);
			prev = row;
			first = false;
		}

		await reader.CloseAsync().ConfigureAwait(false);
		return res;
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

		var res = new List<T> { new() };
		var first = true;
		object[] prev = Array.Empty<object>();
		var properties = new List<PropertyHierarchy>();
		await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
		while (await reader.ReadAsync(token).ConfigureAwait(false))
		{
			var row = new object[reader.FieldCount];
			reader.GetValues(row);
			properties.ForEach(p => p.Reset(prev, row));
			if (first)
			{
				properties = reader.BuildHierarchy<T>(row);
			}

			T? item = res.GetByKeys(properties, row);
			if (item == null)
			{
				break;
			}
			item.Parse(row, properties);
			prev = row;
			first = false;
		}

		await reader.CloseAsync().ConfigureAwait(false);
		return res.First();
	}
}