namespace SimpleOrm.Helpers;

internal sealed class SqlParser
{
	public Statement Statement = Statement.None;

	public bool WillEscape;

	public void Update(char c)
	{
		switch (c)
		{
			case '\\' when !WillEscape:
				WillEscape = true;
				break;
			case '\'' when !WillEscape && Statement is not Statement.String:
				Statement = Statement.String;
				break;
			case '\'' when !WillEscape && Statement is Statement.String:
				Statement = Statement.None;
				break;
			case '`' when Statement is not Statement.String and not Statement.Backticks:
				Statement = Statement.Backticks;
				break;
			case '`' when Statement is not Statement.String and Statement.Backticks:
				Statement = Statement.None;
				break;
			case '[' when Statement is not Statement.String and not Statement.Brackets:
				Statement = Statement.Brackets;
				break;
			case ']' when Statement is not Statement.String and Statement.Brackets:
				Statement = Statement.None;
				break;
			case ':' when Statement is not Statement.String:
				Statement = Statement.Parameter;
				break;
		}
	}
}

internal enum Statement
{
	None,

	String,

	Parameter,

	Backticks,

	Brackets,
}