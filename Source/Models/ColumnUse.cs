using System.Data.Common;

namespace SimpleOrm.Models;

internal sealed class ColumnUse
{
	public readonly DbColumn DbColumn;

	public readonly bool Unsafe;

	public bool Mapped;

	public ColumnUse(DbColumn dbColumn, Action<string>? log)
	{
		DbColumn = dbColumn;
		if (DbColumn.BaseTableName != null)
		{
			return;
		}
		Unsafe = true;
		log?.Invoke($"UNSAFE: Column '{dbColumn.ColumnName}' does not have table information."
					+ " Proceeding with positional mapping");
	}
}