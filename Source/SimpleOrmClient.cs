using System.Collections.ObjectModel;
using System.Data.Common;
using JetBrains.Annotations;
using SimpleOrm.Enums;
using SimpleOrm.Helpers;
using SimpleOrm.Models;

namespace SimpleOrm;

/// <summary>Minimalistic, zero-configuration ORM framework</summary>
/// <typeparam name="TDbConnection">Implementation of <see cref="DbConnection" /> to use</typeparam>
[PublicAPI]
public class SimpleOrmClient<TDbConnection> where TDbConnection : DbConnection, new()
{
	/// <summary>Stored connection string</summary>
	private readonly string _connectionString;

	/// <summary>Log to this</summary>
	public Action<string>? LogTo;

	/// <summary> How many Class/Array properties to map. Set this higher if needed </summary>
	public ushort MaxDepth = 20;

	/// <summary>Create a new Simple ORM client</summary>
	/// <param name="connectionString">
	///     <ul>
	///         <li>MariaDB - $"server={Server};userid={User};password={PW};database={DB}"</li>
	///     </ul>
	/// </param>
	public SimpleOrmClient(string connectionString) => _connectionString = connectionString;

	/// <summary> Return all the results of a query as a list </summary>
	/// <param name="sql">SQL code to execute against database</param>
	/// <param name="params">Object with named parameters matching the ':{Name}' occurrences in the SQL code</param>
	/// <typeparam name="T">Type that conforms to expected results</typeparam>
	/// <returns>The list</returns>
	public List<T> ToList<T>(string sql, object @params) where T : new() =>
				ToListAsync<T>(sql, @params).ConfigureAwait(false).GetAwaiter().GetResult();

	/// <inheritdoc cref="ToList{T}(string,object)" />
	public List<T> ToList<T>(string sql) where T : new() => ToList<T>(sql, new { });

	/// <inheritdoc cref="ToList{T}(string,object)" />
	public async Task<List<T>> ToListAsync<T>(string sql, object @params, CancellationToken token = default)
				where T : new() =>
				await Query<T>(sql.Parameterize(@params, LogTo), false, token).ConfigureAwait(false);

	/// <inheritdoc cref="ToList{T}(string,object)" />
	public async Task<List<T>> ToListAsync<T>(string sql, CancellationToken token = default) where T : new() =>
				await ToListAsync<T>(sql, new { }, token).ConfigureAwait(false);

	/// <summary> Return first fully populated object of type </summary>
	/// <param name="sql">SQL code to execute against database</param>
	/// <param name="params">Object with named parameters matching the ':{Name}' occurrences in the SQL code</param>
	/// <typeparam name="T">Type that conforms to expected results</typeparam>
	/// <returns>The object if query returned any results and default (null) if not</returns>
	public T? FirstOrDefault<T>(string sql, object @params) where T : new() =>
				FirstOrDefaultAsync<T>(sql, @params).ConfigureAwait(false).GetAwaiter().GetResult();

	/// <inheritdoc cref="FirstOrDefault{T}(string,object)" />
	public T? FirstOrDefault<T>(string sql) where T : new() => FirstOrDefault<T>(sql, new { });

	/// <inheritdoc cref="FirstOrDefault{T}(string,object)" />
	public async Task<T?> FirstOrDefaultAsync<T>(string sql, object @params, CancellationToken token = default)
				where T : new()
	{
		var prop = new PropertyHierarchy(typeof(T));
		if (prop.SupportedType == SupportedTypes.Value)
		{
			return await ExecuteScalar<T>(sql, @params, token).ConfigureAwait(false);
		}
		List<T> res = await Query<T>(sql.Parameterize(@params, LogTo), true, token).ConfigureAwait(false);
		return res.FirstOrDefault();
	}

	/// <inheritdoc cref="FirstOrDefault{T}(string,object)" />
	public async Task<T?> FirstOrDefaultAsync<T>(string sql, CancellationToken token = default) where T : new() =>
				await FirstOrDefaultAsync<T>(sql, new { }, token).ConfigureAwait(false);

	/// <summary> Return first fully populated object of type </summary>
	/// <param name="sql">SQL code to execute against database</param>
	/// <param name="params">Object with named parameters matching the ':{Name}' occurrences in the SQL code</param>
	/// <typeparam name="T">Type that conforms to expected results</typeparam>
	/// <returns>The object if query returned any results</returns>
	/// <exception cref="NullReferenceException">If query did not result in a fully populated object</exception>
	public T First<T>(string sql, object @params) where T : new() =>
				FirstAsync<T>(sql, @params).ConfigureAwait(false).GetAwaiter().GetResult();

	/// <inheritdoc cref="First{T}(string, object)" />
	public T First<T>(string sql) where T : new() => First<T>(sql, new { });

	/// <inheritdoc cref="First{T}(string, object)" />
	public async Task<T> FirstAsync<T>(string sql, object @params, CancellationToken token = default) where T : new()
	{
		var prop = new PropertyHierarchy(typeof(T));
		if (prop.SupportedType == SupportedTypes.Value)
		{
			T? scalar = await ExecuteScalar<T>(sql, @params, token).ConfigureAwait(false);
			if (scalar != null)
			{
				return scalar;
			}
		}
		List<T> res = await Query<T>(sql.Parameterize(@params, LogTo), true, token).ConfigureAwait(false);
		if (res.Any())
		{
			return res.First();
		}
		throw new NullReferenceException("Query did not result in a fully populated object");
	}

	/// <inheritdoc cref="First{T}(string, object)" />
	public async Task<T> FirstAsync<T>(string sql, CancellationToken token = default) where T : new() =>
				await FirstAsync<T>(sql, new { }, token).ConfigureAwait(false);

	/// <inheritdoc cref="ToList{T}(string,object)" />
	private async Task<List<T>> Query<T>(string sql, bool breakOnFirst, CancellationToken token) where T : new()
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
		var res = new List<T>();
		var any = false;
		object[] prev = Array.Empty<object>();

		await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
		ReadOnlyCollection<DbColumn> columnInfo = await reader.GetColumnSchemaAsync(token);
		PropertyHierarchy properties = SimpleOrmHelper.BuildAndCheckHierarchy<T>(columnInfo, MaxDepth);
		while (await reader.ReadAsync(token).ConfigureAwait(false))
		{
			var row = new object[reader.FieldCount];
			reader.GetValues(row);
			if (any)
			{
				properties.Reset(prev, row);
			}

			T? item;
			// First iteration GetByKeys will always be null
			if (!any)
			{
				item = new T();
				res.Add(item);
			}
			else
			{
				item = res.GetByKeys(properties, row);
			}
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

				properties.Reset();
			}
			item.Parse(row, properties);
			prev = row;
			any = true;
		}

		await reader.CloseAsync().ConfigureAwait(false);
		return res;
	}

	/// <summary>Fetch a single column of a single row</summary>
	private async Task<T?> ExecuteScalar<T>(string sql, object @params, CancellationToken token)
	{
		await using var connection = new TDbConnection();
		connection.ConnectionString = _connectionString;
		DbCommand command = connection.CreateCommand();
		command.CommandText = sql.Parameterize(@params);
		if (command.Connection == null)
		{
			throw new Exception("Connection could not be established");
		}

		await command.Connection.OpenAsync(token).ConfigureAwait(false);
		object? res = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
		return (T?)res;
	}

	/// <summary>Execute SQL with no return</summary>
	/// <param name="sql">SQL code to execute against database</param>
	/// <param name="params">Object with named parameters matching the ':{Name}' occurrences in the SQL code</param>
	/// <returns>Number of rows affected</returns>
	public int Execute(string sql, object @params) =>
				ExecuteAsync(sql, @params).ConfigureAwait(false).GetAwaiter().GetResult();

	/// <inheritdoc cref="Execute(string,object)" />
	public int Execute(string sql) => Execute(sql, new { });

	/// <inheritdoc cref="Execute(string,object)" />
	public async Task<int> ExecuteAsync(string sql, CancellationToken token = default) =>
				await ExecuteAsync(sql, new { }, token).ConfigureAwait(false);

	/// <inheritdoc cref="Execute(string,object)" />
	public async Task<int> ExecuteAsync(string sql, object @params, CancellationToken token = default)
	{
		await using var connection = new TDbConnection();
		connection.ConnectionString = _connectionString;
		DbCommand command = connection.CreateCommand();
		command.CommandText = sql.Parameterize(@params);
		if (command.Connection == null)
		{
			throw new Exception("Connection could not be established");
		}

		await command.Connection.OpenAsync(token).ConfigureAwait(false);
		return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
	}
}