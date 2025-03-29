using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace YourNamespace.DataAccess.Analysis
{
    public class ApiMetadataAnalyzer
    {
        public List<ControllerMetadata> AnalyzeControllers()
        {
            var results = new List<ControllerMetadata>();
            
            // Get all types in the assembly that are controllers
            var controllerTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(type => type.IsClass && !type.IsAbstract && 
                               type.IsSubclassOf(typeof(ControllerBase)) || 
                               type.Name.EndsWith("Controller"));
            
            foreach (var controllerType in controllerTypes)
            {
                var controllerMetadata = new ControllerMetadata
                {
                    Name = controllerType.Name,
                    FullName = controllerType.FullName,
                    Endpoints = GetEndpointsMetadata(controllerType),
                    Manager = GetManagerMetadata(controllerType)
                };
                
                results.Add(controllerMetadata);
            }
            
            return results;
        }
        
        private List<EndpointMetadata> GetEndpointsMetadata(Type controllerType)
        {
            var endpoints = new List<EndpointMetadata>();
            
            // Get all public methods that have HTTP verb attributes or named as HTTP verbs
            var methodInfos = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType == controllerType && 
                           (m.GetCustomAttributes().Any(attr => attr is HttpMethodAttribute) ||
                            IsConventionalActionMethod(m.Name)));
            
            string controllerRoute = GetControllerRoute(controllerType);
            
            foreach (var methodInfo in methodInfos)
            {
                var endpoint = new EndpointMetadata
                {
                    Name = methodInfo.Name,
                    ReturnType = methodInfo.ReturnType.Name,
                    HttpMethod = GetHttpMethod(methodInfo),
                    Route = BuildFullRoute(controllerRoute, GetMethodRoute(methodInfo), methodInfo.Name),
                    Parameters = GetMethodParameters(methodInfo),
                    StoredProcedureName = ExtractStoredProcedureName(methodInfo)
                };
                
                endpoints.Add(endpoint);
            }
            
            return endpoints;
        }
        
        private string GetControllerRoute(Type controllerType)
        {
            // Check for RouteAttribute or ApiControllerAttribute
            var routeAttr = controllerType.GetCustomAttribute<RouteAttribute>();
            if (routeAttr != null)
                return routeAttr.Template;
            
            // Use conventional routing based on controller name
            string controllerName = controllerType.Name;
            if (controllerName.EndsWith("Controller"))
                controllerName = controllerName.Substring(0, controllerName.Length - "Controller".Length);
            
            return $"api/{controllerName}";
        }
        
        private string GetMethodRoute(MethodInfo methodInfo)
        {
            // Check for route attributes
            var routeAttr = methodInfo.GetCustomAttribute<RouteAttribute>();
            if (routeAttr != null)
                return routeAttr.Template;
            
            // Check for HTTP verb attributes with routes
            var httpMethodAttrs = methodInfo.GetCustomAttributes()
                .Where(attr => attr is HttpMethodAttribute)
                .Cast<HttpMethodAttribute>();
            
            foreach (var attr in httpMethodAttrs)
            {
                if (!string.IsNullOrEmpty(attr.Template))
                    return attr.Template;
            }
            
            // Default to method name for conventional routing
            return methodInfo.Name;
        }
        
        private string BuildFullRoute(string controllerRoute, string methodRoute, string methodName)
        {
            if (string.IsNullOrEmpty(controllerRoute))
                return methodRoute;
                
            if (string.IsNullOrEmpty(methodRoute))
                return $"{controllerRoute}/{methodName}";
                
            // Handle template parameters
            if (methodRoute.StartsWith("/"))
                return methodRoute;
                
            return $"{controllerRoute}/{methodRoute}";
        }
        
        private string GetHttpMethod(MethodInfo methodInfo)
        {
            // Check for explicit HTTP verb attributes
            if (methodInfo.GetCustomAttribute<HttpGetAttribute>() != null) return "GET";
            if (methodInfo.GetCustomAttribute<HttpPostAttribute>() != null) return "POST";
            if (methodInfo.GetCustomAttribute<HttpPutAttribute>() != null) return "PUT";
            if (methodInfo.GetCustomAttribute<HttpDeleteAttribute>() != null) return "DELETE";
            if (methodInfo.GetCustomAttribute<HttpPatchAttribute>() != null) return "PATCH";
            
            // Infer from method name
            if (methodInfo.Name.StartsWith("Get", StringComparison.OrdinalIgnoreCase)) return "GET";
            if (methodInfo.Name.StartsWith("Post", StringComparison.OrdinalIgnoreCase)) return "POST";
            if (methodInfo.Name.StartsWith("Put", StringComparison.OrdinalIgnoreCase)) return "PUT";
            if (methodInfo.Name.StartsWith("Delete", StringComparison.OrdinalIgnoreCase)) return "DELETE";
            if (methodInfo.Name.StartsWith("Patch", StringComparison.OrdinalIgnoreCase)) return "PATCH";
            
            // Default
            return "GET";
        }
        
        private List<ParameterMetadata> GetMethodParameters(MethodInfo methodInfo)
        {
            var parameters = new List<ParameterMetadata>();
            
            foreach (var paramInfo in methodInfo.GetParameters())
            {
                var parameter = new ParameterMetadata
                {
                    Name = paramInfo.Name,
                    Type = paramInfo.ParameterType.Name,
                    FullTypeName = paramInfo.ParameterType.FullName,
                    IsOptional = paramInfo.IsOptional,
                    HasDefaultValue = paramInfo.HasDefaultValue,
                    DefaultValue = paramInfo.HasDefaultValue ? paramInfo.DefaultValue : null,
                    Attributes = paramInfo.GetCustomAttributes().Select(a => a.GetType().Name).ToList()
                };
                
                parameters.Add(parameter);
            }
            
            return parameters;
        }
        
        private bool IsConventionalActionMethod(string methodName)
        {
            // Check if method follows naming conventions for controller actions
            string[] conventionalPrefixes = { "Get", "Post", "Put", "Delete", "Patch", "Head", "Options" };
            return conventionalPrefixes.Any(prefix => 
                methodName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
        
        private string ExtractStoredProcedureName(MethodInfo methodInfo)
        {
            // Check for custom attribute that might contain stored procedure info
            // This is a placeholder - you would need to implement your own logic based on
            // how you associate stored procedures with endpoints in your application
            
            // Example: Look for a custom attribute like [StoredProcedure("dbo.MyProc")]
            var sprocAttribute = methodInfo.GetCustomAttributes()
                .FirstOrDefault(attr => attr.GetType().Name.Contains("StoredProcedure"));
                
            if (sprocAttribute != null)
            {
                // Extract sproc name using reflection on your custom attribute
                var nameProperty = sprocAttribute.GetType().GetProperty("Name");
                if (nameProperty != null)
                {
                    return nameProperty.GetValue(sprocAttribute)?.ToString();
                }
            }
            
            // Alternative: Look for patterns in comments or method naming conventions
            // Example: If your methods follow a convention like GetUser_spDbo_GetUserById
            if (methodInfo.Name.Contains("_sp"))
            {
                var parts = methodInfo.Name.Split(new[] { "_sp" }, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    return "sp" + parts[1].Replace("_", ".");
                }
            }
            
            return null;
        }
        
        private ManagerMetadata GetManagerMetadata(Type controllerType)
        {
            // Look for constructor with IxxxManager parameter
            var constructor = controllerType.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();
                
            if (constructor == null)
                return null;
                
            // Find manager parameter (assuming it follows the IXxxManager naming convention)
            var managerParameter = constructor.GetParameters()
                .FirstOrDefault(p => p.ParameterType.IsInterface && 
                                  p.ParameterType.Name.Contains("Manager"));
                
            if (managerParameter == null)
                return null;
                
            var managerType = managerParameter.ParameterType;
            
            return new ManagerMetadata
            {
                Name = managerType.Name,
                FullName = managerType.FullName,
                Methods = GetManagerMethods(managerType)
            };
        }
        
        private List<MethodMetadata> GetManagerMethods(Type managerType)
        {
            var methods = new List<MethodMetadata>();
            
            // Get all methods from the interface
            var methodInfos = managerType.GetMethods();
            
            foreach (var methodInfo in methodInfos)
            {
                var method = new MethodMetadata
                {
                    Name = methodInfo.Name,
                    ReturnType = methodInfo.ReturnType.Name,
                    Parameters = GetMethodParameters(methodInfo),
                    StoredProcedureName = ExtractStoredProcedureName(methodInfo)
                };
                
                methods.Add(method);
            }
            
            return methods;
        }
    }
    
    // Model classes to hold metadata
    public class ControllerMetadata
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public List<EndpointMetadata> Endpoints { get; set; } = new List<EndpointMetadata>();
        public ManagerMetadata Manager { get; set; }
    }
    
    public class EndpointMetadata
    {
        public string Name { get; set; }
        public string HttpMethod { get; set; }
        public string Route { get; set; }
        public string ReturnType { get; set; }
        public List<ParameterMetadata> Parameters { get; set; } = new List<ParameterMetadata>();
        public string StoredProcedureName { get; set; }
    }
    
    public class ManagerMetadata
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public List<MethodMetadata> Methods { get; set; } = new List<MethodMetadata>();
    }
    
    public class MethodMetadata
    {
        public string Name { get; set; }
        public string ReturnType { get; set; }
        public List<ParameterMetadata> Parameters { get; set; } = new List<ParameterMetadata>();
        public string StoredProcedureName { get; set; }
    }
    
    public class ParameterMetadata
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string FullTypeName { get; set; }
        public bool IsOptional { get; set; }
        public bool HasDefaultValue { get; set; }
        public object DefaultValue { get; set; }
        public List<string> Attributes { get; set; } = new List<string>();
    }
}
