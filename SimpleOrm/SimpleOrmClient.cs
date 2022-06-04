using System.Data;
using System.Data.Common;
using System.Reflection;

namespace SimpleOrm;

public class SimpleOrmClient<TDbConnection> where TDbConnection : DbConnection, new()
{
	private readonly string _connectionString;

	public SimpleOrmClient(string connectionString) => _connectionString = connectionString;

	public async Task<List<T>> QueryAsync<T>(string sql, CancellationToken token = default) where T : new()
	{
		await using var connection = new TDbConnection();
		connection.ConnectionString = _connectionString;
		DbCommand command = connection.CreateCommand();
		command.CommandText = sql;

		var res = new List<T>();
		if (command.Connection == null)
		{
			return res;
		}
		await command.Connection.OpenAsync(token).ConfigureAwait(false);

		PropertyInfo[] check = { };
		var first = true;
		await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
		while (await reader.ReadAsync(token).ConfigureAwait(false))
		{
			var row = new object[reader.FieldCount];
			reader.GetValues(row);
			if (first)
			{
				check = FirstTimeCheck<T>(row, reader);
			}
			var el = InstantiateFromRow<T>(row, check, reader.FieldCount);
			res.Add(el);
			first = false;
		}

		await reader.CloseAsync().ConfigureAwait(false);
		return res;
	}

	private static PropertyInfo[] FirstTimeCheck<T>(IReadOnlyList<object> columns, IDataRecord reader) where T : new()
	{
		PropertyInfo[] properties = typeof(T).GetProperties();
		var res = new PropertyInfo[reader.FieldCount];
		for (var i = 0; i < reader.FieldCount; i++)
		{
			string name = reader.GetName(i);
			Type actual = columns[i].GetType();
			PropertyInfo? truth = properties.FirstOrDefault(
					info => String.Equals(name, info.Name, StringComparison.OrdinalIgnoreCase));

			if (truth == null)
			{
				throw new Exception($"{name} was not found as a property in type {typeof(T).Name}");
			}
			if (!actual.IsAssignableFrom(truth.PropertyType))
			{
				throw new Exception($"{actual.Name} is not assignable from {truth.PropertyType.Name}");
			}
			res[i] = truth;
		}
		return res;
	}

	private static string GetTest()
	{
		return "";
	}

	private static T InstantiateFromRow<T>(
			IReadOnlyList<object> values,
			IReadOnlyList<PropertyInfo> properties,
			int fieldCount) where T : new()
	{
		var el = new T();
		for (var i = 0; i < fieldCount; i++)
		{
			properties[i].SetValue(el, values[i]);
		}
		return el;
	}
}