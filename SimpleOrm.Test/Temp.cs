using System;
using System.Text.Json;
using MySqlConnector;
using SimpleOrm.Test.Models;

namespace SimpleOrm.Test;

public static class Temp
{
	private static readonly SimpleOrmClient<MySqlConnection> Db = new(
			"server=localhost;userid=zoru;password=1012;database=test");

	public static void Run()
	{
		Root? res = Db.FirstOrDefault<Root>(
						@"
select *
from root r
         join sibling s on s.Id = r.SiblingId
       left  join child c on r.Id = c.RootId
		")
				.ConfigureAwait(false).GetAwaiter().GetResult();
		Console.WriteLine(JsonSerializer.Serialize(res));
	}
}