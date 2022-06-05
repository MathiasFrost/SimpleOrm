using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using SimpleOrm.Test.Models;
using Xunit;

namespace SimpleOrm.Test;

public class SimpleOrmTest
{
	private readonly SimpleOrmClient<MySqlConnection> _db;

	public SimpleOrmTest() =>
			_db = new SimpleOrmClient<MySqlConnection>("server=localhost;userid=zoru;password=1012;database=test");

	[Fact]
	public async Task Should_FetchFirstOrDefault()
	{
		Root? res = await _db.FirstOrDefault<Root>(
						@"
select *
from root r
         join sibling s on s.Id = r.SiblingId
         join child c on r.Id = c.RootId
		")
				.ConfigureAwait(false);
		Assert.NotNull(res);
	}
	
	[Fact]
	public async Task Should_FetchAll()
	{
		IList<Root> res = await _db.QueryAsync<Root>(
						@"
select *
from root r
         join sibling s on s.Id = r.SiblingId
         join child c on r.Id = c.RootId
		")
				.ConfigureAwait(false);
		Assert.NotEmpty(res);
	}
}