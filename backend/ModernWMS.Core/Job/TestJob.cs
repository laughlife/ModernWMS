using Hangfire;

namespace ModernWMS.Core.Job
{
    /// <summary>
    /// 
    /// </summary>
    public class TestJob : IJob
    {
        /// <summary>
        /// 
        /// </summary>
        private IHttpClientFactory _httpClient;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="httpClient"></param>
        public TestJob(IHttpClientFactory httpClient) {
            this._httpClient = httpClient;
        }

        /// <summary>
        /// 
        /// </summary>
        public string CronExpression => Cron.Hourly(3);

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task Execute()
        {
            using (HttpClient httpClient = new HttpClient())
            {
               
                httpClient.DefaultRequestHeaders.Add("Method", "Post");
                await httpClient.PostAsync("https://wmsonline.ikeyly.com:20011/hello-world?culture=zh-cn", null);

                //HttpResponseMessage response = await httpClient.PostAsync("https://wmsonline.ikeyly.com:20011/hello-world?culture=zh-cn", null);
                //if (response.StatusCode == HttpStatusCode.OK)
                //{
                //   var data =  await response.Content.ReadAsStringAsync();
                   
                //}

            }
        }


    }
}
