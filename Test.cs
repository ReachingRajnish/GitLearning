using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Apttus.DocGen.Domain.Util {
    public class RestClient {
        private readonly Encoding encoding = Encoding.UTF8;

        public string _authToken { get; set; }

        public RestClient() {

        }

        public RestClient(string authToken) {
            this._authToken = authToken;
        }

        public async Task<string> ExecuteRestAsync(string uri, string methodType) {
            HttpWebRequest httpWebRequest = CreateHttpRequest(uri, methodType, _authToken);
            Task<string> response;
            using(HttpWebResponse myHttpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
            using(Stream stream = myHttpWebResponse.GetResponseStream())
            using(StreamReader reader = new StreamReader(stream)) {
                response = reader.ReadToEndAsync();
            }

            return await response;

        }

        public async Task<string> PostRequestAsync(string uri, string authToken, string json) {
            HttpWebRequest httpWebRequest = CreateHttpRequest(uri, "POST", authToken);
            httpWebRequest.ContentType = "application/json; charset=UTF-8";
            byte[] data = Encoding.UTF8.GetBytes(json);

            Stream requestStream = httpWebRequest.GetRequestStream();
            requestStream.Write(data, 0, data.Length);
            requestStream.Close();

            HttpWebResponse myHttpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
            Stream responseStream = myHttpWebResponse.GetResponseStream();
            StreamReader myStreamReader = new StreamReader(responseStream, Encoding.Default);

            Task<string> Response = myStreamReader.ReadToEndAsync();

            myStreamReader.Close();
            responseStream.Close();
            myHttpWebResponse.Close();

            return await Response;
        }

        public async Task<string> PostRequestForContentLocationAsync(string uri, string authToken, string json) {
            HttpWebRequest httpWebRequest = CreateHttpRequest(uri, "POST", authToken);
            httpWebRequest.ContentType = "application/json; charset=UTF-8";
            byte[] data = Encoding.UTF8.GetBytes(json);

            Stream requestStream = httpWebRequest.GetRequestStream();
            await requestStream.WriteAsync(data, 0, data.Length);
            requestStream.Close();

            HttpWebResponse myHttpWebResponse = (HttpWebResponse)(await httpWebRequest.GetResponseAsync());
            string ContentLocation = myHttpWebResponse.ResponseUri.ToString();
            myHttpWebResponse.Close();

            return ContentLocation;

        }

        public async Task<string> uploadFile(string requestUrl, Dictionary<string, object> postParameters, byte[] file, string fileName, string contentType) {

            postParameters.Add("file", new RestClient.FileParameter(file, fileName, contentType));

            // Create request and receive response
            HttpWebResponse webResponse = await MultipartFormDataPost(requestUrl, "docgen", postParameters);

            // Process response
            StreamReader responseReader = new StreamReader(webResponse.GetResponseStream());
            string fullResponse = responseReader.ReadToEnd();
            webResponse.Close();

            return fullResponse;

        }

        private HttpWebRequest CreateHttpRequest(string uri, string methodType, string authToken) {
            HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(uri);
            httpWebRequest.ServicePoint.Expect100Continue = false;
            httpWebRequest.Method = methodType;
            httpWebRequest.KeepAlive = true;
            httpWebRequest.Headers.Add("Authorization", "Bearer " + authToken);

            httpWebRequest.Headers.Add("Accept-Language", "en-US,en;q=0.8");
            httpWebRequest.Accept = "application/json, text/plain,text/html , */*";
            httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            httpWebRequest.AllowAutoRedirect = false;

            return httpWebRequest;
        }

        private byte[] GetMultipartFormData(Dictionary<string, object> postParameters, string boundary) {
            Stream formDataStream = new System.IO.MemoryStream();
            bool needsCLRF = false;

            foreach(var param in postParameters) {
                if(needsCLRF)
                    formDataStream.Write(encoding.GetBytes("\r\n"), 0, encoding.GetByteCount("\r\n"));

                needsCLRF = true;

                if(param.Value is FileParameter) {
                    FileParameter fileToUpload = (FileParameter)param.Value;

                    // Add just the first part of this param, since we will write the file data directly to the Stream
                    string header = string.Format("--{0}\r\nContent-Disposition: form-data; name=\"{1}\"; filename=\"{2}\"\r\nContent-Type: {3}\r\n\r\n",
                        boundary,
                        param.Key,
                        fileToUpload.FileName ?? param.Key,
                        fileToUpload.ContentType ?? "application/octet-stream");

                    formDataStream.Write(encoding.GetBytes(header), 0, encoding.GetByteCount(header));

                    // Write the file data directly to the Stream, rather than serializing it to a string.
                    formDataStream.Write(fileToUpload.File, 0, fileToUpload.File.Length);
                } else {
                    string postData = string.Format("--{0}\r\nContent-Disposition: form-data; name=\"{1}\"\r\n\r\n{2}",
                        boundary,
                        param.Key,
                        param.Value);
                    formDataStream.Write(encoding.GetBytes(postData), 0, encoding.GetByteCount(postData));
                }
            }

            // Add the end of the request.  Start with a newline
            string footer = "\r\n--" + boundary + "--\r\n";
            formDataStream.Write(encoding.GetBytes(footer), 0, encoding.GetByteCount(footer));

            // Dump the Stream into a byte[]
            formDataStream.Position = 0;
            byte[] formData = new byte[formDataStream.Length];
            formDataStream.Read(formData, 0, formData.Length);
            formDataStream.Close();

            return formData;
        }

        private async Task<HttpWebResponse> MultipartFormDataPost(string postUrl, string userAgent, Dictionary<string, object> postParameters) {
            string formDataBoundary = String.Format("----------{0:N}", Guid.NewGuid());
            string contentType = "multipart/form-data; boundary=" + formDataBoundary;

            byte[] formData = GetMultipartFormData(postParameters, formDataBoundary);

            return await PostForm(postUrl, userAgent, contentType, formData);
        }

        private async Task<HttpWebResponse> PostForm(string postUrl, string userAgent, string contentType, byte[] formData) {
            HttpWebRequest request = WebRequest.Create(postUrl) as HttpWebRequest;

            if(request == null) {
                throw new NullReferenceException("request is not a http request");
            }

            request.Method = "POST";
            request.ContentType = contentType;
            request.UserAgent = userAgent;
            request.CookieContainer = new CookieContainer();
            request.ContentLength = formData.Length;


            request.Headers.Add("Authorization", "Bearer " + _authToken);

            using(Stream requestStream = request.GetRequestStream()) {
                requestStream.Write(formData, 0, formData.Length);
                requestStream.Close();
            }

            return await request.GetResponseAsync() as HttpWebResponse;
        }

        public class FileParameter {
            public byte[] File { get; set; }
            public string FileName { get; set; }
            public string ContentType { get; set; }
            public FileParameter(byte[] file) : this(file, null) { }
            public FileParameter(byte[] file, string filename) : this(file, filename, null) { }
            public FileParameter(byte[] file, string filename, string contenttype) {
                File = file;
                FileName = filename;
                ContentType = contenttype;
            }
        }
    }
}
