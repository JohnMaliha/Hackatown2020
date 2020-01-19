

namespace Gentec
{

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.ServiceBus;
    using Newtonsoft.Json;

    class Program
    {
        const string ServiceBusConnectionString = "Endpoint=sb://licenseplatepublisher.servicebus.windows.net/;SharedAccessKeyName=ConsumeReads;SharedAccessKey=VNcJZVQAVMazTAfrssP6Irzlg/pKwbwfnOqMXqROtCQ=";
        const string TopicName = "licenseplateread";
        const string SubscriptionName = "v7JUXbzZJ69ACb58";
        static ISubscriptionClient subscriptionClient;
        static ISubscriptionClient subscriptionClient2;
        const string ServiceBusConnectionString2 = "Endpoint=sb://licenseplatepublisher.servicebus.windows.net/;SharedAccessKeyName=listeneronly;SharedAccessKey=w+ifeMSBq1AQkedLCpMa8ut5c6bJzJxqHuX9Jx2XGOk=";
        const string TopicName2 = "wantedplatelistupdate";
        const string SubscriptionName2 = "bCNa4e5sJnP7zCD6";

        static List<string> plaques;

        delegate void notification();
        static notification notify;

        static void update()
        {
            plaques = getWantedList();
        }

        public static async Task Main(string[] args)
        {
            subscriptionClient = new SubscriptionClient(ServiceBusConnectionString, TopicName, SubscriptionName);

            subscriptionClient2 = new SubscriptionClient(ServiceBusConnectionString2, TopicName2, SubscriptionName2);
            update();

            Console.WriteLine("======================================================");
            Console.WriteLine("Press ENTER key to exit after receiving all the messages.");
            Console.WriteLine("======================================================");
       
            // Register subscription message handler and receive messages in a loop
            RegisterOnMessageHandlerAndReceiveMessages();

            Console.ReadKey();

            await subscriptionClient.CloseAsync();
        }

        static void RegisterOnMessageHandlerAndReceiveMessages()
        {
            // Configure the message handler options in terms of exception handling, number of concurrent messages to deliver, etc.
            var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
            {
                // Maximum number of concurrent calls to the callback ProcessMessagesAsync(), set to 1 for simplicity.
                // Set it according to how many messages the application wants to process in parallel.
                MaxConcurrentCalls = 1,

                // Indicates whether MessagePump should automatically complete the messages after returning from User Callback.
                // False below indicates the Complete will be handled by the User Callback as in `ProcessMessagesAsync` below.
                AutoComplete = false
            };

            // Register the function that processes messages.
            subscriptionClient.RegisterMessageHandler(ProcessMessagesAsync, messageHandlerOptions);

            subscriptionClient2.RegisterMessageHandler(ProcessMessagesAsync2, messageHandlerOptions);
        }

        static List<string> getWantedList()
        {
            string url = "https://licenseplatevalidator.azurewebsites.net/api/lpr/wantedplates";
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Headers["Authorization"] = "Basic ZXF1aXBlNjA6eEE5TDZHV0g2WVpxM2VTcg==";
            Console.WriteLine("lance");
            var response = (HttpWebResponse)request.GetResponse();


               // var resp = new StreamReader(response.GetResponseStream()).ReadToEnd();
                //var messageFromServer = resp;

                Stream receiveStream = response.GetResponseStream();
                StreamReader readStream = new StreamReader(receiveStream, Encoding.UTF8);
                string result = readStream.ReadToEnd();
                Console.WriteLine(result);
            List<string> values = JsonConvert.DeserializeObject<List<string>>(result);

            return values;
        }

        static bool isWanted(string value, List<string> values)
        {
            foreach (string e in values)
            {
                if (e.Equals(value))
                    return true;
            
            }
            return false;
        }
        static async Task ProcessMessagesAsync(Message message, CancellationToken token)
        {
            
            
            // Process the message.
            // Console.WriteLine($"Received message: SequenceNumber:{message.SystemProperties.SequenceNumber} Body:{Encoding.UTF8.GetString(message.Body)}");

            Dictionary<string, Object> values = JsonConvert.DeserializeObject<Dictionary<String, Object>>(Encoding.UTF8.GetString(message.Body));
            
            //Console.WriteLine($" {values["Latitude"]} {values["LicensePlateCaptureTime"]} {values["Longitude"]} {values["LicensePlate"]}");

            Dictionary<string, Object> data = new Dictionary<string, Object>()
            {
                { "LicensePlateCaptureTime", values["LicensePlateCaptureTime"]},
                { "LicensePlate", values["LicensePlate"]},
                { "Latitude", values["Latitude"]},
                { "Longitude", values["Longitude"]},
            };
            if(isWanted((string)values["LicensePlate"],plaques))
            { 

            string jsonData = JsonConvert.SerializeObject(data);
            Console.WriteLine(jsonData);
            byte[] byteData=Encoding.UTF8.GetBytes(jsonData);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://licenseplatevalidator.azurewebsites.net/api/lpr/platelocation");
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers["Authorization"] = "Basic ZXF1aXBlNjA6eEE5TDZHV0g2WVpxM2VTcg==";
            try
            {
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(byteData, 0, byteData.Length);
                }

                var response = (HttpWebResponse)request.GetResponse();
                var responseString = new StreamReader(response.GetResponseStream()).ReadToEnd();

                Console.WriteLine(responseString);
            }
            catch (WebException e)
            {
                Console.WriteLine(e.Message);

            }
            }

            // Complete the message so that it is not received again.
            // This can be done only if the subscriptionClient is created in ReceiveMode.PeekLock mode (which is the default).
            await subscriptionClient.CompleteAsync(message.SystemProperties.LockToken);

            // Note: Use the cancellationToken passed as necessary to determine if the subscriptionClient has already been closed.
            // If subscriptionClient has already been closed, you can choose to not call CompleteAsync() or AbandonAsync() etc.
            // to avoid unnecessary exceptions.
        }



        static async Task ProcessMessagesAsync2(Message message, CancellationToken token)
        {

            update();

            // Complete the message so that it is not received again.
            // This can be done only if the subscriptionClient is created in ReceiveMode.PeekLock mode (which is the default).
            await subscriptionClient2.CompleteAsync(message.SystemProperties.LockToken);

            // Note: Use the cancellationToken passed as necessary to determine if the subscriptionClient has already been closed.
            // If subscriptionClient has already been closed, you can choose to not call CompleteAsync() or AbandonAsync() etc.
            // to avoid unnecessary exceptions.
        }




        static Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
        {
            Console.WriteLine($"Message handler encountered an exception {exceptionReceivedEventArgs.Exception}.");
            var context = exceptionReceivedEventArgs.ExceptionReceivedContext;
            Console.WriteLine("Exception context for troubleshooting:");
            Console.WriteLine($"- Endpoint: {context.Endpoint}");
            Console.WriteLine($"- Entity Path: {context.EntityPath}");
            Console.WriteLine($"- Executing Action: {context.Action}");
            return Task.CompletedTask;
        }
    }
}