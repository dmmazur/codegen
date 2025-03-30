using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Microsoft.AspNetCore.Mvc;

public class EndpointMethodAnalyzer
{
    public static Dictionary<string, List<string>> GetManagerMethodCalls(string assemblyPath)
    {
        var result = new Dictionary<string, List<string>>();
        
        // Load the assembly
        var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        
        // Find controller types
        var controllerTypes = assembly.MainModule.Types
            .Where(t => t.BaseType != null && 
                        t.BaseType.FullName.Contains("ControllerBase") && 
                        !t.IsAbstract);
                        
        foreach (var controllerType in controllerTypes)
        {
            // Find endpoints (methods with Http* attributes)
            var endpoints = controllerType.Methods
                .Where(m => m.CustomAttributes.Any(a => a.AttributeType.Name.Contains("Http") || 
                                                       a.AttributeType.Name == "RouteAttribute"));
                                                       
            foreach (var endpoint in endpoints)
            {
                if (!endpoint.HasBody)
                    continue;
                    
                var endpointKey = $"{controllerType.Name}.{endpoint.Name}";
                result[endpointKey] = new List<string>();
                
                // Analyze method body for calls to _manager methods
                foreach (var instruction in endpoint.Body.Instructions)
                {
                    // Look for calls/callvirt instructions
                    if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                    {
                        var methodReference = instruction.Operand as MethodReference;
                        if (methodReference != null)
                        {
                            // Check if the method is called on a field named _manager
                            if (IsMethodCalledOnManagerField(instruction, endpoint))
                            {
                                result[endpointKey].Add(methodReference.Name);
                            }
                        }
                    }
                }
            }
        }
        
        return result;
    }
    
    private static bool IsMethodCalledOnManagerField(Instruction callInstruction, MethodDefinition method)
    {
        // Go back through instructions to find the field that's being called
        if (callInstruction.Previous?.OpCode == OpCodes.Ldarg_0)
        {
            // Direct call to _manager without intermediate instructions
            return true;
        }
        
        // This is simplified - a real implementation would need more sophisticated IL analysis
        var prevInst = callInstruction.Previous;
        while (prevInst != null)
        {
            if (prevInst.OpCode == OpCodes.Ldfld)
            {
                var fieldRef = prevInst.Operand as FieldReference;
                if (fieldRef != null && fieldRef.Name.Contains("_manager"))
                {
                    return true;
                }
            }
            prevInst = prevInst.Previous;
        }
        
        return false;
    }
}
