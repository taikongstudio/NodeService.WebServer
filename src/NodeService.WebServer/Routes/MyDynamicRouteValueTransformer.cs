using Microsoft.AspNetCore.Mvc.Routing;
using Quartz.Util;

namespace NodeService.WebServer.Routes
{
    public class MyDynamicRouteValueTransformer : DynamicRouteValueTransformer
    {

        public MyDynamicRouteValueTransformer()
        {
        }

        public override ValueTask<RouteValueDictionary> TransformAsync(HttpContext httpContext, RouteValueDictionary values)
        {
            if (values.TryGetValue("controller", out var controllerValue) && controllerValue is string controller)
            {
                if (controller.Equals("CommonConfig", StringComparison.OrdinalIgnoreCase))
                {
                    var result = new RouteValueDictionary(values);
                    result["controller"] = "Configuration";
                    return ValueTask.FromResult(result);
                }
            }
            return ValueTask.FromResult(values);
        }
    }
}
