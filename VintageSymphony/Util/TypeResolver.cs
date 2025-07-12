using System.Reflection;

namespace VintageSymphony.Util;

/// <summary>
/// Utility class for resolving types at runtime
/// </summary>
public static class TypeResolver
{
	/// <summary>
	/// Resolves all non-abstract classes that implement the specified interface and creates instances using the default constructor
	/// </summary>
	/// <typeparam name="T">The interface type to look for</typeparam>
	/// <returns>A list of instantiated objects that implement the interface</returns>
	public static List<T> ResolveAll<T>() where T : class
	{
		var result = new List<T>();
		var interfaceType = typeof(T);

		// Get all types from the current assembly
		var assembly = Assembly.GetExecutingAssembly();
		var types = assembly.GetTypes()
			.Where(t => interfaceType.IsAssignableFrom(t)
			            && t is { IsInterface: false, IsAbstract: false }
			            && t.GetConstructor(Type.EmptyTypes) != null);

		foreach (var type in types)
		{
			// Create an instance using the default constructor
			if (Activator.CreateInstance(type) is T instance)
			{
				result.Add(instance);
			}
		}

		return result;
	}
}