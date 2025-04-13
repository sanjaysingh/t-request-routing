using System.Web.Http;

namespace RequestRouting
{
    /// <summary>
    /// Configures the Web API routes and services.
    /// </summary>
    public static class WebApiConfig
    {
        /// <summary>
        /// Registers Web API configuration.
        /// </summary>
        /// <param name="config">The HTTP configuration</param>
        public static void Register(HttpConfiguration config)
        {
            // Enable attribute routing
            config.MapHttpAttributeRoutes();

            // Configure conventional routing
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
