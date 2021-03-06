using System;
using SimpleOrm.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace SimpleOrm.Test;

public class ParseTest
{
	private const string Sql = "select `Wierd:Name Test` from test where Name <> :Name";

	private const string Truth = @"select `Wierd:Name Test` from test where Name <> '\'; delete * from test; --'";

	private readonly ITestOutputHelper _testOutputHelper;

	public ParseTest(ITestOutputHelper testOutputHelper) => _testOutputHelper = testOutputHelper;

	[Fact]
	public void ShouldParseCorrectly()
	{
		DateTime start = DateTime.Now;

		string actual = Sql.Parameterize(new { Name = "'; delete * from test; --" }, _testOutputHelper.WriteLine);

		var ms = (DateTime.Now - start).TotalMilliseconds.ToString("F");
		_testOutputHelper.WriteLine($"Parsing took {ms}ms");

		Assert.Equal(Truth, actual);
	}
}