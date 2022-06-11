using System.Collections;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Reflection;
using SimpleOrm.Enums;

namespace SimpleOrm.Models;

internal class PropertyHierarchy
{
	public readonly DbColumn? DbColumn;

	public readonly bool IsNullable;

	public readonly PropertyHierarchy? Parent;

	private readonly PropertyInfo? _propertyInfo;

	private readonly string? _schema;

	private readonly string? _table;

	public readonly Type? GenericType;

	public readonly SupportedTypes SupportedType;

	public readonly Type Type;

	public List<PropertyHierarchy> Children = new();

	public bool ValueSet;

	/// <summary>Constructor for root</summary>
	public PropertyHierarchy(Type type)
	{
		Type = type;
		SupportedType = GetSupportedType(type);
		IsNullable = CheckIfNullable(type);
		(_schema, _table) = GetTableAndSchemaName(type);
	}

	/// <summary>Constructor for props</summary>
	public PropertyHierarchy(PropertyInfo info, PropertyHierarchy parent, IEnumerable<DbColumn> dbColumns)
	{
		Parent = parent;
		_propertyInfo = info;
		Type = info.PropertyType;
		IsNullable = CheckIfNullable(Type);
		SupportedType = GetSupportedType(Type);
		if (SupportedType == SupportedTypes.Array)
		{
			GenericType = info.PropertyType.GenericTypeArguments.First();
			(_schema, _table) = GetTableAndSchemaName(GenericType);
		}
		else
		{
			(_schema, _table) = GetTableAndSchemaName(Type);
		}

		string column = GetColumnName(info);
		DbColumn = dbColumns.FirstOrDefault(dbColumn =>
					(parent._schema == null || dbColumn.BaseSchemaName == parent._schema)
					&& dbColumn.BaseTableName == parent._table
					&& dbColumn.ColumnName == column);
	}

	public bool IsKey =>
				DbColumn != null && (DbColumn?.IsKey ?? throw new Exception("DbColumn did not have IsKey set"));

	public int Index => DbColumn?.ColumnOrdinal ?? throw new Exception("DbColumn did not have ColumnOrdinal set");

	private static bool CheckIfNullable(Type type)
	{
		bool isNullable = Nullable.GetUnderlyingType(type) != null;
		if (isNullable)
		{
			return isNullable;
		}
		const string nullableAttribute = "System.Runtime.CompilerServices.NullableAttribute";
		isNullable = type.CustomAttributes.Any(data => data.AttributeType.FullName == nullableAttribute);
		return isNullable;
	}

	private static SupportedTypes GetSupportedType(Type type)
	{
		if (type.IsValueType || typeof(string).IsAssignableFrom(type))
		{
			return SupportedTypes.Value;
		}
		if (typeof(IEnumerable).IsAssignableFrom(type))
		{
			return SupportedTypes.Array;
		}
		if (type.IsClass)
		{
			return SupportedTypes.Class;
		}
		throw new Exception("Property was not found to be of SupportedType Value, Array nor Class");
	}

	private static (string?, string) GetTableAndSchemaName(MemberInfo type)
	{
		IList<CustomAttributeData> data = type.GetCustomAttributesData();
		string tableName = type.Name;
		string? schemaName = null;
		foreach (CustomAttributeData attributeData in data)
		{
			if (!typeof(TableAttribute).IsAssignableFrom(attributeData.AttributeType))
			{
				continue;
			}
			var table = attributeData.ConstructorArguments.FirstOrDefault().Value?.ToString();
			if (table != null)
			{
				tableName = table;
			}
			var schema = attributeData.NamedArguments.FirstOrDefault().TypedValue.Value?.ToString();
			if (schema != null)
			{
				schemaName = schema;
			}
		}

		return (schemaName, tableName);
	}

	public ConstructorInfo GetConstructor()
	{
		ConstructorInfo? constructor = Type.GetConstructor(Array.Empty<Type>());
		if (constructor == null)
		{
			throw new Exception($"Class property {Type.Name} did not have a parameterless constructor");
		}
		return constructor;
	}

	public ConstructorInfo GetGenericConstructor()
	{
		ConstructorInfo? constructor = GenericType?.GetConstructor(Array.Empty<Type>());
		if (constructor == null)
		{
			throw new Exception($"Property {GenericType?.Name} did not have a parameterless constructor");
		}
		return constructor;
	}

	public void Reset(object[] prev, object[] curr)
	{
		// ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
		switch (SupportedType)
		{
			case SupportedTypes.Value when IsNewValue(prev, curr):
			case SupportedTypes.Class when Children.All(c => c.IsNewValue(prev, curr)):
				ValueSet = false;
				break;
		}
		foreach (PropertyHierarchy child in Children)
		{
			child.Reset(prev, curr);
		}
	}

	public void Reset()
	{
		ValueSet = false;
		foreach (PropertyHierarchy child in Children)
		{
			child.Reset();
		}
	}

	public void SetValue(object item, object[] columns)
	{
		if (DbColumn == null)
		{
			if (!IsNullable && Parent?.IsNullable != true)
			{
				throw new Exception($"{_propertyInfo!.Name} is not nullable but query returned null");
			}
		}
		else
		{
			_propertyInfo!.SetValue(item, columns[Index]);
		}
	}

	public void SetValue(object item, object value)
	{
		if (Children.All(hierarchy => hierarchy.DbColumn == null))
		{
			if (!IsNullable)
			{
				throw new Exception($"{_propertyInfo!.Name} is not nullable but query returned null");
			}
		}
		else
		{
			_propertyInfo!.SetValue(item, value);
		}
	}

	public object? GetValue(object item) => _propertyInfo?.GetValue(item);

	private bool IsNewValue(IReadOnlyList<object> prev, IReadOnlyList<object> curr)
	{
		if (!IsKey)
		{
			return true;
		}
		int index = Index;
		return !Equals(prev[index], curr[index]);
	}

	private static string GetColumnName(MemberInfo propertyInfo)
	{
		IList<CustomAttributeData> data = propertyInfo.GetCustomAttributesData();
		foreach (CustomAttributeData attributeData in data)
		{
			if (!typeof(ColumnAttribute).IsAssignableFrom(attributeData.AttributeType))
			{
				continue;
			}
			var name = attributeData.ConstructorArguments.First().Value?.ToString();
			if (name != null)
			{
				return name;
			}
		}

		return propertyInfo.Name;
	}
}