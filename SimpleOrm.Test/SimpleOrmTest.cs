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
select r.*
from root r
         join child c on r.Id = c.RootId
         join sibling s on s.Id = r.SiblingId
		")
				.ConfigureAwait(false);
		Assert.NotNull(res);
	}
}