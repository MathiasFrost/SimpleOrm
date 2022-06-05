using System;
using System.Collections.Generic;
using System.Text.Json;
using JetBrains.Annotations;
using MySqlConnector;
using SimpleOrm.Test.Models;

namespace SimpleOrm.Test;

[PublicAPI]
public static class Temp
{
	private static readonly SimpleOrmClient<MySqlConnection> Db = new(
			"server=localhost;userid=zoru;password=1012;database=test");

	public static void Run()
	{
		var res = Db.First<Root>(
				@"
select *
from root r
         join sibling s on s.Id = r.SiblingId
         join child c on r.Id = c.RootId
		");
		Console.WriteLine(JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }));
	}
}