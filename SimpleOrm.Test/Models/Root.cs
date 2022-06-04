using System.ComponentModel.DataAnnotations.Schema;
using JetBrains.Annotations;

namespace SimpleOrm.Test.Models;

[PublicAPI]
public class Root
{
	public ulong Id { get; init; }

	[Column("Name")]
	public string Namae { get; init; } = null!;
}