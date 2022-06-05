using System.Data.Common;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using SimpleOrm.Helpers;
using SimpleOrm.Models;

namespace SimpleOrm;

/// <summary>Minimalistic, zero-configuration ORM framework</summary>
/// <typeparam name="TDbConnection">Implementation of <see cref="DbConnection"/> to use</typeparam>
[PublicAPI]
public class SimpleOrmClient<TDbConnection> where TDbConnection : DbConnection, new()
{
	/// <summary>Stored connection string</summary>
	private readonly string _connectionString;

	/// <summary>Create a new Simple ORM client</summary>
	/// <param name="connectionString">
	/// <ul>
	///		<li>MariaDB - $"server={Server};userid={User};password={PW};database={DB}"</li>
	/// </ul>
	/// </param>
	public SimpleOrmClient(string connectionString) => _connectionString = connectionString;

	/// <summary> Return all the results of a query as a list </summary>
	/// <param name="sql">SQL code to execute against database</param>
	/// <typeparam name="T">Type that conforms to expected results</typeparam>
	/// <returns>The list</returns>
	public List<T> ToList<T>(string sql) where T : new() =>
			ToListAsync<T>(sql).ConfigureAwait(false).GetAwaiter().GetResult();

	/// <inheritdoc cref="ToList{T}"/>
	public async Task<List<T>> ToListAsync<T>(string sql, CancellationToken token = default) where T : new() =>
			await Query<T>(sql, false, token).ConfigureAwait(false);

	/// <summary> Return first fully populated object of type </summary>
	/// <param name="sql">SQL code to execute against database</param>
	/// <typeparam name="T">Type that conforms to expected results</typeparam>
	/// <returns>The object</returns>
	public T First<T>(string sql) where T : new() => FirstAsync<T>(sql).ConfigureAwait(false).GetAwaiter().GetResult();

	/// <inheritdoc cref="First{T}"/>
	public async Task<T> FirstAsync<T>(string sql, CancellationToken token = default) where T : new()
	{
		List<T> res = await Query<T>(sql, true, token).ConfigureAwait(false);
		return res.First();
	}

	/// <inheritdoc cref="ToList{T}"/>
	private async Task<List<T>> Query<T>(string sql, bool breakOnFound, CancellationToken token = default)
			where T : new()
	{
		var connection = new TDbConnection();
		await using ConfiguredAsyncDisposable _ = connection.ConfigureAwait(false);
		connection.ConnectionString = _connectionString;
		DbCommand command = connection.CreateCommand();
		command.CommandText = sql;
		if (command.Connection == null)
		{
			throw new Exception("Connection could not be established");
		}
		await command.Connection.OpenAsync(token).ConfigureAwait(false);

		var res = new List<T> { new() };
		var first = true;
		object[] prev = Array.Empty<object>();
		var properties = new List<PropertyHierarchy>();
		DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable __ = reader.ConfigureAwait(false);
		while (await reader.ReadAsync(token).ConfigureAwait(false))
		{
			var row = new object[reader.FieldCount];
			reader.GetValues(row);
			properties.ForEach(p => p.Reset(prev, row));
			if (first)
			{
				properties = reader.BuildHierarchy<T>(row);
			}

			// First iteration will always be null
			T? item = first ? res.First() : res.GetByKeys(properties, row);
			if (item == null)
			{
				// We only want first full result here
				if (breakOnFound)
				{
					break;
				}

				// Else we want all results
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
}