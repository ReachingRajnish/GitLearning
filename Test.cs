    public class MergeServiceClient : IMergeServiceClient {
        protected readonly IProductSettingUtil productSettingsUtil;

        public MergeServiceClient(IProductSettingUtil productSettings) {
            this.productSettingsUtil = productSettings;
        }

        /// <summary>
        /// Sends the asynchronous.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns></returns>
        public async Task<string> SendAsync(string data) {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            var dataPacket = Encoding.UTF8.GetBytes(data); // this should be UFT32 need to recheck
            var mergeServerUrl = await productSettingsUtil.GetMergeServerUrlAsync();
            var timeout = await productSettingsUtil.GetMergeServerTimeOutAsync();

            var req = (HttpWebRequest)WebRequest.Create(mergeServerUrl);
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            req.ContentLength = dataPacket.Length;
            req.Timeout = timeout;

            using(var stream = req.GetRequestStream()) {
                stream.Write(dataPacket, 0, dataPacket.Length);
            }

            using(var res = (HttpWebResponse)req.GetResponse()) {
                using(var streams = res.GetResponseStream()) {
                    var reader = new StreamReader(streams, Encoding.UTF8);
                    return reader.ReadToEnd();
                }
            }
        }
    }
