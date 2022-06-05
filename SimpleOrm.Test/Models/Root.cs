using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using JetBrains.Annotations;

namespace SimpleOrm.Test.Models;

[PublicAPI]
public class Root
{
	public ulong Id { get; init; }

	public string Name { get; init; } = null!;

	public ulong SiblingId { get; init; }

	public Sibling? Sibling { get; init; }

	// public List<Child> Children { get; init; } = new();
}

[PublicAPI]
public class Sibling
{
	public ulong Id { get; init; }

	public string Name { get; init; } = null!;
}

[PublicAPI]
public class Child
{
	public ulong Id { get; init; }

	public ulong RootId { get; init; }

	public string Name { get; init; } = null!;
}