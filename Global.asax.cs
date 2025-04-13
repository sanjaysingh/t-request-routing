using System.Web.Http;

namespace RequestRouting
{
    /// <summary>
    /// Web API application entry point.
    /// </summary>
    public class WebApiApplication : System.Web.HttpApplication
    {
        /// <summary>
        /// Configures the application at startup.
        /// </summary>
        protected void Application_Start()
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);
        }
    }
}
