using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using SimpleOrm.Test.Models;
using Xunit;
using Xunit.Abstractions;

namespace SimpleOrm.Test;

public class SimpleOrmTest
{
	public const string ConnectionString = "server=localhost;userid=zoru;password=1012;database=test";

	public const string Sql = @"
select *
from root r
        join sibling s on s.Id = r.SiblingId
        join child c on r.Id = c.RootId
        left join grandchild gc on c.Id = gc.ChildId
";

	private const string NoResults = Sql + "where r.Id = -1";

	private readonly SimpleOrmClient<MySqlConnection> _db;

	private readonly ITestOutputHelper _testOutputHelper;

	public SimpleOrmTest(ITestOutputHelper testOutputHelper)
	{
		_testOutputHelper = testOutputHelper;
		_db = new SimpleOrmClient<MySqlConnection>(ConnectionString) { LogTo = _testOutputHelper.WriteLine };
	}

	[Fact]
	public async Task Should_FetchFirstOrDefault()
	{
		Root res = await _db.FirstAsync<Root>(Sql, new { }).ConfigureAwait(false);
		Assert.NotNull(res);
	}

	[Fact]
	public async Task ShouldBeNull_IfNoResults()
	{
		Root? @null = await _db.FirstOrDefaultAsync<Root>(NoResults, new { }).ConfigureAwait(false);
		Assert.Null(@null);
	}

	[Fact]
	public async Task Should_FetchAll()
	{
		DateTime start = DateTime.Now;

		// Simple Orm query
		IList<Root> res = await _db.ToListAsync<Root>(Sql, new { }).ConfigureAwait(false);

		Assert.NotEmpty(res);

		var ms = (DateTime.Now - start).TotalMilliseconds.ToString("F");
		_testOutputHelper.WriteLine($"SimpleOrm query took {ms}ms");

		// Naked query for comparison
		start = DateTime.Now;

		await using var connection = new MySqlConnection(ConnectionString);
		connection.ConnectionString = ConnectionString;
		MySqlCommand command = connection.CreateCommand();
		command.CommandText = Sql;
		if (command.Connection == null)
		{
			throw new Exception("Connection could not be established");
		}

		// ReSharper disable once CollectionNeverQueried.Local
		var list = new List<object>();
		await command.Connection.OpenAsync().ConfigureAwait(false);
		await using MySqlDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
		while (await reader.ReadAsync().ConfigureAwait(false))
		{
			var row = new object[reader.FieldCount];
			reader.GetValues(row);
			list.Add(row);
		}
		await reader.CloseAsync().ConfigureAwait(false);

		ms = (DateTime.Now - start).TotalMilliseconds.ToString("F");
		_testOutputHelper.WriteLine($"Naked query took {ms}ms");

		Assert.NotEmpty(res);
	}

	[Fact]
	public async Task ShouldFetchScalar()
	{
		var res = await _db.FirstAsync<string>(@"select Name from root");
		Assert.NotEmpty(res);
	}

	[Fact]
	public async Task ShouldExecuteSuccessfully()
	{
		int rows = await _db.ExecuteAsync(@"update root set Name = :name where Id = :id", new {id = 1, name = "Some root"});
		Assert.True(rows == 1);
	}
}