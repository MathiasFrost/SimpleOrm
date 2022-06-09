using System.Data.Common;
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
	/// <param name="params">Object with named parameters matching the ':{Name}' occurrences in the SQL code</param>
	/// <typeparam name="T">Type that conforms to expected results</typeparam>
	/// <returns>The list</returns>
	public List<T> ToList<T>(string sql, object @params) where T : new() =>
				ToListAsync<T>(sql, @params).ConfigureAwait(false).GetAwaiter().GetResult();

	/// <inheritdoc cref="ToList{T}"/>
	public async Task<List<T>> ToListAsync<T>(string sql, object @params, CancellationToken token = default)
				where T : new() =>
				await Query<T>(sql.Parameterize(@params), false, token).ConfigureAwait(false);

	/// <summary> Return first fully populated object of type </summary>
	/// <param name="sql">SQL code to execute against database</param>
	/// <param name="params">Object with named parameters matching the ':{Name}' occurrences in the SQL code</param>
	/// <typeparam name="T">Type that conforms to expected results</typeparam>
	/// <returns>The object</returns>
	public T FirstOrDefault<T>(string sql, object @params) where T : new() =>
				FirstOrDefaultAsync<T>(sql, @params).ConfigureAwait(false).GetAwaiter().GetResult();

	/// <inheritdoc cref="FirstOrDefault{T}"/>
	public async Task<T> FirstOrDefaultAsync<T>(string sql, object @params, CancellationToken token = default)
				where T : new()
	{
		List<T> res = await Query<T>(sql.Parameterize(@params), true, token).ConfigureAwait(false);
		return res.First();
	}

	/// <inheritdoc cref="ToList{T}"/>
	private async Task<List<T>> Query<T>(string sql, bool breakOnFirst, CancellationToken token = default)
				where T : new()
	{
		await using var connection = new TDbConnection();
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

			// First iteration will always be null
			T? item = first ? res.First() : res.GetByKeys(properties, row);
			if (item == null)
			{
				// We only want first full result here
				if (breakOnFirst)
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