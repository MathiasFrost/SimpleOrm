using SimpleOrm.Helpers;
using Xunit;

namespace SimpleOrm.Test;

public class ParseTest
{
	private const string Sql = "select `Wierd:Name Test` from test where Name <> :Name";

	private const string Truth = @"select `Wierd:Name Test` from test where Name <> '\'; delete * from test; --'";
	
		[Fact]
	public void ShouldParseCorrectly()
	{
		string actual = Sql.Parameterize(new {Name = "'; delete * from test; --"});
		Assert.Equal(Truth, actual);
	}
}