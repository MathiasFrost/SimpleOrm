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
	public List<T> ToList<T>(string sql, object @params) =>
				ToListAsync<T>(sql, @params).ConfigureAwait(false).GetAwaiter().GetResult();

	/// <inheritdoc cref="ToList{T}(string,object)" />
	public List<T> ToList<T>(string sql) => ToList<T>(sql, new { });

	/// <inheritdoc cref="ToList{T}(string,object)" />
	public async Task<List<T>> ToListAsync<T>(string sql, object @params, CancellationToken token = default) =>
				await Query<T>(sql.Parameterize(@params, LogTo), false, token).ConfigureAwait(false);

	/// <inheritdoc cref="ToList{T}(string,object)" />
	public async Task<List<T>> ToListAsync<T>(string sql, CancellationToken token = default) =>
				await ToListAsync<T>(sql, new { }, token).ConfigureAwait(false);

	/// <summary> Return first fully populated object of type </summary>
	/// <param name="sql">SQL code to execute against database</param>
	/// <param name="params">Object with named parameters matching the ':{Name}' occurrences in the SQL code</param>
	/// <typeparam name="T">Type that conforms to expected results</typeparam>
	/// <returns>The object if query returned any results and default (null) if not</returns>
	public T? FirstOrDefault<T>(string sql, object @params) =>
				FirstOrDefaultAsync<T>(sql, @params).ConfigureAwait(false).GetAwaiter().GetResult();

	/// <inheritdoc cref="FirstOrDefault{T}(string,object)" />
	public T? FirstOrDefault<T>(string sql) => FirstOrDefault<T>(sql, new { });

	/// <inheritdoc cref="FirstOrDefault{T}(string,object)" />
	public async Task<T?> FirstOrDefaultAsync<T>(string sql, object @params, CancellationToken token = default)
	{
		var prop = new PropertyHierarchy(typeof(T));
		switch (prop.SupportedType)
		{
			case SupportedTypes.Array: throw new Exception("Arrays are not supported");
			case SupportedTypes.Value: return await ExecuteScalar<T>(sql, @params, token).ConfigureAwait(false);
			case SupportedTypes.Class:
			{
				List<T> res = await Query<T>(sql.Parameterize(@params, LogTo), true, token).ConfigureAwait(false);
				return res.FirstOrDefault();
			}
			default: throw new ArgumentOutOfRangeException(nameof(prop), "Unexpected");
		}
	}

	/// <inheritdoc cref="FirstOrDefault{T}(string,object)" />
	public async Task<T?> FirstOrDefaultAsync<T>(string sql, CancellationToken token = default) =>
				await FirstOrDefaultAsync<T>(sql, new { }, token).ConfigureAwait(false);

	/// <summary> Return first fully populated object of type </summary>
	/// <param name="sql">SQL code to execute against database</param>
	/// <param name="params">Object with named parameters matching the ':{Name}' occurrences in the SQL code</param>
	/// <typeparam name="T">Type that conforms to expected results</typeparam>
	/// <returns>The object if query returned any results</returns>
	/// <exception cref="NullReferenceException">If query did not result in a fully populated object</exception>
	public T First<T>(string sql, object @params) =>
				FirstAsync<T>(sql, @params).ConfigureAwait(false).GetAwaiter().GetResult();

	/// <inheritdoc cref="First{T}(string, object)" />
	public T First<T>(string sql) => First<T>(sql, new { });

	/// <inheritdoc cref="First{T}(string, object)" />
	public async Task<T> FirstAsync<T>(string sql, object @params, CancellationToken token = default)
	{
		T? res = await FirstOrDefaultAsync<T>(sql, @params, token).ConfigureAwait(false);
		return res ?? throw new NullReferenceException("Query did not result in a fully populated object");
	}

	/// <inheritdoc cref="First{T}(string, object)" />
	public async Task<T> FirstAsync<T>(string sql, CancellationToken token = default) =>
				await FirstAsync<T>(sql, new { }, token).ConfigureAwait(false);

	/// <inheritdoc cref="ToList{T}(string,object)" />
	private async Task<List<T>> Query<T>(string sql, bool breakOnFirst, CancellationToken token)
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
		List<ColumnUse> columnUses = columnInfo.Select(column => new ColumnUse(column, LogTo)).ToList();
		PropertyHierarchy hierarchy = SimpleOrmHelper.BuildAndCheckHierarchy<T>(columnUses, MaxDepth);
		
		while (await reader.ReadAsync(token).ConfigureAwait(false))
		{
			var row = new object[reader.FieldCount];
			reader.GetValues(row);
			if (any)
			{
				hierarchy.Reset(prev, row);
			}

			T? item;
			// First iteration GetByKeys will always be null
			if (!any)
			{
				item = (T)hierarchy.Constructor!.Invoke(Array.Empty<object>());
				res.Add(item);
			}
			else
			{
				item = (T?)((IEnumerable<object>)res).GetByKeys(hierarchy, row);
			}

			// If GetByKeys is null we have a new root element
			if (item == null)
			{
				// We only want first full result here
				if (breakOnFirst)
				{
					break;
				}

				// Else we want all results
				item = (T)hierarchy.Constructor!.Invoke(Array.Empty<object>());
				res.Add(item);

				hierarchy.Reset();
			}
			item!.Parse(row, hierarchy);
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
		command.CommandText = sql.Parameterize(@params, LogTo);
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
		command.CommandText = sql.Parameterize(@params, LogTo);
		if (command.Connection == null)
		{
			throw new Exception("Connection could not be established");
		}

		await command.Connection.OpenAsync(token).ConfigureAwait(false);
		return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
	}
}