# Simple ORM

Simple, zero-configuration object relational mapping framework based on DbConnection.

This framework does not make assumptions about the relationships between tables in your database, but rather takes the
result of an SQL query and infers, from the supplied type, where values are supposed to go.

## Examples

Consider the model:

```csharp
[PublicAPI, Table("root")]
public class Root
{
	public ulong Id { get; init; }

	public string Name { get; init; } = null!;

	public ulong SiblingId { get; init; }

	public Sibling? Sibling { get; init; }

	public List<Child> Children { get; init; } = new();
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

SimpleOrm will map the flat values onto the property matching the schema, table and column name[^1] and assumes that
rows with the same keys[^2] represents another element for `Root.Children`, resulting in the object:

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

[^1]: _(Case sensitive. Schema is by default not specified and Table name is the name of the class or the one specified in `[Table("Name")]`. Column is the name of the property or the one specified in `[Column("Name")]`)_
[^2]: _(Keys are fetched from DB info. If there are no keys, it will treat all base columns as keys)_
