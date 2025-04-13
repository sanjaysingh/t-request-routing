using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI;

namespace RequestRouting
{
    /// <summary>
    /// Server endpoint that handles both GET and POST requests.
    /// </summary>
    public partial class dctserver : Page
    {
        private const string StatusXml = "<status>Server is live and ready to accept POST requests</status>";
        private const string ResponseXml = 
            "<server>\n" +
            "   <responses>\n" +
            "      <oneRs>\n" +
            "          <payload/>\n" +
            "      </oneRs>\n" +
            "      <twoRs>\n" +
            "          <payload/>\n" +
            "      </twoRs>\n" +
            "   </responses>\n" +
            "</server>";

        protected void Page_Load(object sender, EventArgs e)
        {
            switch (Request.HttpMethod)
            {
                case "GET":
                    HandleGetRequest();
                    break;
                case "POST":
                    HandlePostRequest();
                    break;
                default:
                    SendMethodNotAllowed();
                    break;
            }
        }

        private void HandleGetRequest()
        {
            SendXmlResponse(StatusXml);
        }

        private void HandlePostRequest()
        {
            // Simulate processing delay
            Task.Delay(100).Wait();
            
            // In a real implementation, process the request body:
            // var requestBody = ReadRequestBody();
            
            SendXmlResponse(ResponseXml);
        }

        private void SendXmlResponse(string xml)
        {
            Response.Clear();
            Response.ContentType = "application/xml";
            Response.StatusCode = 200;
            Response.Write(xml);
            Response.End();
        }

        private void SendMethodNotAllowed()
        {
            Response.Clear();
            Response.StatusCode = 405;
            Response.StatusDescription = "Method Not Allowed";
            Response.End();
        }

        private string ReadRequestBody()
        {
            using (var reader = new StreamReader(Request.InputStream, Request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }
    }
} 