public static class DynamicInterfaceImplementationFactory
{
    private static readonly AssemblyBuilder _assemblyBuilder;
    private static readonly ModuleBuilder _moduleBuilder;
    
    static DynamicInterfaceImplementationFactory()
    {
        var assemblyName = new AssemblyName("DynamicImplementations");
        _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        _moduleBuilder = _assemblyBuilder.DefineDynamicModule("MainModule");
    }
    
    public static Type CreateImplementationType<TInterface>() where TInterface : class
    {
        var interfaceType = typeof(TInterface);
        
        if (!interfaceType.IsInterface)
            throw new ArgumentException($"{interfaceType.Name} must be an interface");
            
        var typeName = $"{interfaceType.Name}DynamicImpl_{Guid.NewGuid():N}";
        var typeBuilder = _moduleBuilder.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Class,
            typeof(object));
            
        // Implement the interface
        typeBuilder.AddInterfaceImplementation(interfaceType);
        
        // Create constructor
        var constructorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
            
        var constructorIL = constructorBuilder.GetILGenerator();
        constructorIL.Emit(OpCodes.Ldarg_0);
        constructorIL.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        constructorIL.Emit(OpCodes.Ret);
        
        // Implement all interface methods
        var methods = interfaceType.GetMethods();
        foreach (var method in methods)
        {
            CreateMethodImplementation(typeBuilder, method);
        }
        
        // Handle inherited interface methods
        foreach (var baseInterface in interfaceType.GetInterfaces())
        {
            var baseMethods = baseInterface.GetMethods();
            foreach (var method in baseMethods)
            {
                if (!methods.Any(m => m.Name == method.Name && 
                    m.GetParameters().Select(p => p.ParameterType).SequenceEqual(
                        method.GetParameters().Select(p => p.ParameterType))))
                {
                    CreateMethodImplementation(typeBuilder, method);
                }
            }
        }
        
        return typeBuilder.CreateType()!;
    }
    
    private static void CreateMethodImplementation(TypeBuilder typeBuilder, MethodInfo interfaceMethod)
    {
        var parameterTypes = interfaceMethod.GetParameters().Select(p => p.ParameterType).ToArray();
        
        var methodBuilder = typeBuilder.DefineMethod(
            interfaceMethod.Name,
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            interfaceMethod.ReturnType,
            parameterTypes);
            
        var il = methodBuilder.GetILGenerator();
        
        // Create exception message with method name
        var exceptionMessage = $"Method '{interfaceMethod.Name}' was called on dynamically generated implementation";
        
        // Load exception message onto stack
        il.Emit(OpCodes.Ldstr, exceptionMessage);
        
        // Create and throw NotImplementedException
        il.Emit(OpCodes.Newobj, typeof(NotImplementedException).GetConstructor(new[] { typeof(string) })!);
        il.Emit(OpCodes.Throw);
        
        // Override the interface method
        typeBuilder.DefineMethodOverride(methodBuilder, interfaceMethod);
    }
    
    public static TInterface CreateInstance<TInterface>() where TInterface : class
    {
        var implementationType = CreateImplementationType<TInterface>();
        return (TInterface)Activator.CreateInstance(implementationType)!;
    }
}
