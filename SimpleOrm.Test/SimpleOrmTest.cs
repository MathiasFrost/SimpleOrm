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
			_db = new SimpleOrmClient<MySqlConnection>(
					"server=localhost;userid=zoru;password=1012;database=beingrating");

	[Fact]
	public async Task Should_FetchArray()
	{
		List<Root> res = await _db.QueryAsync<Root>(
						@"
select b.Id, b.Name, b.Gender, b.Tags, u.Name, u.Description
from beings b
         left join universes u on u.Id = b.UniverseId;
		")
				.ConfigureAwait(false);
		Assert.NotEmpty(res);
	}
}