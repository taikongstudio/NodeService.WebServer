using NodeService.WebServer.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NodeService.WebServer.Services.Notifications
{
    internal class LarkNotificationHandler
    {
        private class tenant_access_token_req
        {
            public string app_id { get; set; }

            public string app_secret { get; set; }
        }

        private class tanant_access_token_rsp
        {
            public int code { get; set; }

            public string msg { get; set; }

            public string tenant_access_token { get; set; }

            public int expire { get; set; }
        }

        private class open_id_req
        {
            public bool include_resigned { get; set; }
            public string[] mobiles { get; set; }
        }

        private class user_list_item
        {
            public string mobile { get; set; }

            public string user_id { get; set; }
        }

        private class data
        {
            public user_list_item[] user_list { get; set; }
        }

        private class open_id_rsp
        {
            public int code { get; set; }

            public string msg { get; set; }

            public data data { get; set; }
        }

        private class message_content
        {
            public string text { get; set; }
        }


        private class message
        {
            public string msg_type { get; set; }

            public string receive_id { get; set; }

            public string content { get; set; }
        }




        public class send_message_rsp
        {
            public class Body
            {
                /// <summary>
                /// 
                /// </summary>
                public string content { get; set; }
            }

            public class Sender
            {
                /// <summary>
                /// 
                /// </summary>
                public string id { get; set; }
                /// <summary>
                /// 
                /// </summary>
                public string id_type { get; set; }
                /// <summary>
                /// 
                /// </summary>
                public string sender_type { get; set; }
                /// <summary>
                /// 
                /// </summary>
                public string tenant_key { get; set; }
            }


            public class Data
            {
                /// <summary>
                /// 
                /// </summary>
                public Body body { get; set; }
                /// <summary>
                /// 
                /// </summary>
                public string chat_id { get; set; }
                /// <summary>
                /// 
                /// </summary>
                public string create_time { get; set; }
                /// <summary>
                /// 
                /// </summary>
                public bool deleted { get; set; }
                /// <summary>
                /// 
                /// </summary>
                public string message_id { get; set; }
                /// <summary>
                /// 
                /// </summary>
                public string msg_type { get; set; }
                /// <summary>
                /// 
                /// </summary>
                public Sender sender { get; set; }
                /// <summary>
                /// 
                /// </summary>
                public string update_time { get; set; }
                /// <summary>
                /// 
                /// </summary>
                public bool updated { get; set; }
            }

            /// <summary>
            /// 
            /// </summary>
            public int code { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public Data data { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string msg { get; set; }
        }


        public class Template_variable
        {
            /// <summary>
            /// 
            /// </summary>
            public string date { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string name { get; set; }
        }

        public class Data
        {
            /// <summary>
            /// 
            /// </summary>
            public string template_id { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public Template_variable template_variable { get; set; }
        }

        public class interactive_content
        {
            /// <summary>
            /// 
            /// </summary>
            public string type { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public Data data { get; set; }
        }



        public async Task<NotificationResult> HandleAsync(NotificationMessage notificationMessage)
        {
            var notificationResult = new NotificationResult();
            if (notificationMessage.Content.Index != 1)
            {
                return notificationResult; 
            }

            try
            {
                if (!notificationMessage.Configuration.Options.TryGetValue(
                        NotificationConfigurationType.Lark,
                        out var larkOptionsValue) || larkOptionsValue is not LarkNotificationOptions options)
                {
                    if (larkOptionsValue is JsonElement jsonElement)
                    {
                        options = jsonElement.Deserialize<LarkNotificationOptions>(new JsonSerializerOptions()
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    else
                    {
                        if (Debugger.IsAttached) return notificationResult;
                        throw new InvalidOperationException("Email notification is not supported");
                    }
                }
                using var httpClient = new HttpClient()
                {
                    BaseAddress = new Uri("https://open.feishu.cn"),
                };
                var accessTokenRsp = await httpClient.PostAsJsonAsync("/open-apis/auth/v3/tenant_access_token/internal", new tenant_access_token_req()
                {
                    app_id = options.AppId,
                    app_secret = options.AppSecret
                });
                var str = await accessTokenRsp.Content.ReadAsStringAsync();

                var get_token_rsp = await accessTokenRsp.Content.ReadFromJsonAsync<tanant_access_token_rsp>();
                if (get_token_rsp.code != 0)
                {
                    notificationResult.ErrorCode = get_token_rsp.code;
                    notificationResult.Message = get_token_rsp.msg;
                    return notificationResult;
                }
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", get_token_rsp.tenant_access_token);

                using var getOpenIdReq = new HttpRequestMessage();
                getOpenIdReq.Method = HttpMethod.Post;
                getOpenIdReq.RequestUri = new Uri("https://open.feishu.cn/open-apis/contact/v3/users/batch_get_id?user_id_type=user_id");

                getOpenIdReq.Content = JsonContent.Create(new open_id_req()
                {
                    include_resigned = true,
                    mobiles = options.Recievers.Select(x => x.Value).ToArray()
                });


                using var openIdRsp = await httpClient.SendAsync(getOpenIdReq, default);

                var open_id_rsp = await openIdRsp.Content.ReadFromJsonAsync<open_id_rsp>();

                if (open_id_rsp.code != 0)
                {
                    throw new Exception(open_id_rsp.msg);
                }

                foreach (var user_list_item in open_id_rsp.data.user_list)
                {

                    foreach (var entryArray in notificationMessage.Content.AsT1.Entries.Chunk(20))
                    {
                        if (entryArray == null)
                        {
                            continue;
                        }

                        foreach (var item in entryArray)
                        {
                            using var sendMessageReq = new HttpRequestMessage();
                            sendMessageReq.Method = HttpMethod.Post;
                            sendMessageReq.RequestUri = new Uri("https://open.feishu.cn/open-apis/im/v1/messages?receive_id_type=user_id");

                            sendMessageReq.Content = JsonContent.Create(new message()
                            {
                                msg_type = "interactive",
                                content = JsonSerializer.Serialize(new interactive_content()
                                {
                                    type = "template",
                                    data = new Data()
                                    {
                                        template_id = "AAq0dGsSmpZ6T",
                                        template_variable = new Template_variable()
                                        {
                                            date = item.Name,
                                            name = item.Value

                                        }
                                    }
                                }),
                                receive_id = user_list_item.user_id,
                            });

                            var httpSendMessageRsp = await httpClient.SendAsync(sendMessageReq, default);
                            var result = await httpSendMessageRsp.Content.ReadAsStringAsync();

                            var sendMessageRsp = await httpSendMessageRsp.Content.ReadFromJsonAsync<send_message_rsp>();

                            if (sendMessageRsp.code != 0)
                            {
                                throw new Exception(sendMessageRsp.msg);
                            }
                        }

                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }


                }

            }
            catch (Exception ex)
            {

                throw;
            }
            return notificationResult;
        }

    }
}
