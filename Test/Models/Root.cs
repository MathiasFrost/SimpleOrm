using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using JetBrains.Annotations;

namespace SimpleOrm.Test.Models;

[PublicAPI, Table("root")]
public class Root
{
	public ulong Id { get; init; }

	public string Name { get; init; } = null!;

	public ulong SiblingId { get; init; }

	public Sibling? Sibling { get; init; }

	public IList<Child> Children { get; init; } = new List<Child>();

	public string Test1 { get; } = "test";

	public string Test2 => "test";

	private string Test3 { get; set; } = "test";

	[NotMapped] public string Test4 { get; set; } = "test";
}

[PublicAPI, Table("sibling")]
public class Sibling
{
	public ulong Id { get; init; }

	public string Name { get; init; } = null!;
}

[PublicAPI, Table("child")]
public class Child
{
	public ulong Id { get; init; }

	public ulong RootId { get; init; }

	public string Name { get; init; } = null!;

	public List<Grandchild> Grandchildren { get; init; } = new();
}

[PublicAPI, Table("grandchild")]
public class Grandchild
{
	public ulong Id { get; init; }

	public ulong ChildId { get; init; }

	public DateTime? Date { get; init; }
}