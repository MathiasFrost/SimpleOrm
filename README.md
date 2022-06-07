# Simple ORM

Simple, zero-configuration object relational mapping framework based on DbConnection.

This framework does not make assumptions about the relationships between tables in your database, but rather takes the
result of an SQL query and infers, from the supplied type, where class and array properties are supposed to go.

## Examples

Consider the model:

```csharp
public class Root
{
	[Key] public ulong Id { get; init; }

	public string Name { get; init; } = null!;

	public ulong SiblingId { get; init; }

	public Sibling? Sibling { get; init; }

	public List<Child> Children { get; init; } = new();
}

public class Sibling
{
	public ulong Id { get; init; }

	public string Name { get; init; } = null!;
}

public class Child
{
	public ulong Id { get; init; }

	public ulong RootId { get; init; }

	public string Name { get; init; } = null!;
}
```

We can utilize SimpleOrm to instantiate this model with SQL:

```csharp
IList<Root> res = await _db.ToListAsync<Root>(@"
select *
from root r
         join sibling s on s.Id = r.SiblingId
         join child c on r.Id = c.RootId
		", token).ConfigureAwait(false);
```

SimpleOrm will map the flat values onto the first property it can find[^1] and assumes that rows with the same key[^2]
represents another element for `Root.Children`, resulting in the object:

```json
[
  {
    "Id": 1,
    "Name": "Some Root",
    "SiblingId": 1,
    "Sibling": {
      "Id": 1,
      "Name": "Some Sibling"
    },
    "Children": [
      {
        "Id": 1,
        "RootId": 1,
        "Name": "Some Child"
      },
      {
        "Id": 2,
        "RootId": 1,
        "Name": "Some Other CHild"
      },
      {
        "Id": 3,
        "RootId": 1,
        "Name": "Third child"
      }
    ]
  },
  {
    "Id": 2,
    "Name": "Another root",
    "SiblingId": 1,
    "Sibling": {
      "Id": 1,
      "Name": "Some Sibling"
    },
    "Children": [
      {
        "Id": 4,
        "RootId": 2,
        "Name": "Second Root child"
      }
    ]
  }
]
```

[^1]: _(this means that the position of columns with the same name matters. Note that `[Column("Name")]` can be used)_
[^2]: _(if no properties are marked with `[Key]`, it will assume all base properties are key)_
